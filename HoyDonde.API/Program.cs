using FirebaseAdmin;
using Google.Api.Gax;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using HoyDonde.API.Authentication;
using HoyDonde.API.Authorization;
using HoyDonde.API.Commands;
using HoyDonde.API.Middleware;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configuración de Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/hoydonde.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Configuración de Firebase: la credencial se carga una sola vez acá y se reutiliza tanto
// para Firebase Admin (FirebaseApp) como para Firestore (abajo), en vez de dejar que
// FirestoreDb dependa de una fuente de credenciales distinta e implícita.
var firebaseCredentialsPath = builder.Configuration["Firebase:CredentialsPath"] ?? "firebase-service-account.json";
GoogleCredential? firebaseCredential = null;
if (File.Exists(firebaseCredentialsPath))
{
    firebaseCredential = GoogleCredential.FromFile(firebaseCredentialsPath);
    // WebApplicationFactory<Program> reejecuta este Program.cs una vez por cada instancia de
    // TestApplicationFactory/EmulatorWebApplicationFactory creada en el mismo proceso xunit, y
    // xunit corre clases de test en paralelo por default; FirebaseApp.Create solo puede
    // llamarse una vez por proceso para la app "default" (lanza ArgumentException si ya
    // existe). Un check-then-act simple (if DefaultInstance == null) no es thread-safe acá:
    // dos hilos pueden leer null antes de que cualquiera cree la app. El catch de abajo hace
    // la operación idempotente sin ocultar un ArgumentException real y distinto.
    try
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = firebaseCredential
        });
    }
    catch (ArgumentException) when (FirebaseApp.DefaultInstance != null)
    {
    }
}
else
{
    Log.Warning("Firebase credentials file not found at {Path}. App may fail to authenticate.", firebaseCredentialsPath);
}

// Configuración de Firestore: antes usaba FirestoreDb.Create(projectId), que ignora
// Firebase:CredentialsPath por completo y exige GOOGLE_APPLICATION_CREDENTIALS
// (Application Default Credentials) para autenticar fuera del emulador -de ahí que hubiera
// que exportar esa variable a mano en cada sesión-. Ahora reutiliza la misma credencial de
// archivo que Firebase Admin (arriba) vía FirestoreDbBuilder.GoogleCredential.
// EmulatorDetection.EmulatorOrProduction preserva el comportamiento contra Firestore
// Emulator (FIRESTORE_EMULATOR_HOST) sin tocar los fixtures de test: tanto
// TestApplicationFactory como EmulatorWebApplicationFactory reemplazan este singleton
// FirestoreDb ANTES de que algo lo resuelva (es un registro AddSingleton con factory
// lazy), así que el throw de abajo solo puede dispararse en una ejecución real
// (dotnet run) sin emulador y sin credencial válida - nunca durante `dotnet test`.
builder.Services.AddSingleton<FirestoreDb>(provider =>
{
    var projectId = builder.Configuration["Firebase:ProjectId"];
    if (string.IsNullOrEmpty(projectId)) throw new Exception("Firebase ProjectId is missing in configuration.");

    var usingFirestoreEmulator = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST"));

    var firestoreDbBuilder = new FirestoreDbBuilder
    {
        ProjectId = projectId,
        EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
    };

    if (!usingFirestoreEmulator)
    {
        if (firebaseCredential == null)
        {
            throw new InvalidOperationException(
                $"No se encontró la credencial de Firebase en '{firebaseCredentialsPath}' (configuración Firebase:CredentialsPath). " +
                "Es necesaria para autenticar Firestore fuera del Firestore Emulator. Colocá tu cuenta de servicio en esa ruta, " +
                "ajustá Firebase:CredentialsPath en appsettings, o configurá FIRESTORE_EMULATOR_HOST para desarrollar contra el emulador.");
        }

        firestoreDbBuilder.GoogleCredential = firebaseCredential;
    }

    return firestoreDbBuilder.Build();
});

// Configuración de autenticación: verifica el ID token de Firebase con el Admin SDK
// (FirebaseAuth.DefaultInstance.VerifyIdTokenAsync vía IFirebaseIdTokenVerifier), no con
// AddJwtBearer/Authority contra securetoken.google.com -ese enfoque fallaba con
// SecurityTokenSignatureKeyNotFoundException (IDX10500) al no resolver las claves públicas-.
// Solo autentica (UID); la autorización sigue resolviéndose exclusivamente contra Firestore
// (AccionAuthorizationHandler/IPermissionService), nunca desde un claim del token.
builder.Services.AddSingleton<IFirebaseIdTokenVerifier, FirebaseIdTokenVerifier>();
builder.Services.AddAuthentication(FirebaseAuthenticationDefaults.AuthenticationScheme)
.AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>(
    FirebaseAuthenticationDefaults.AuthenticationScheme, options => { });

// Agregar servicios de autorización: una policy por cada Accion del catálogo
// (docs/security-refactor-plan.md §3, Etapa 5). AccionAuthorizationHandler resuelve cada policy
// exclusivamente contra IPermissionService; el claim legacy "role" no interviene acá.
builder.Services.AddScoped<IAuthorizationHandler, AccionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var accion in Acciones.Todas)
    {
        options.AddPolicy(accion, policy => policy.Requirements.Add(new AccionRequirement(accion)));
    }
});

// Agregar controladores. Los enums HTTP de Event/Ticket (EventStatus/EventEffectiveStatus/
// EventCategory/TicketStatus) viajan siempre como el nombre del miembro (p. ej. "Publicado",
// "Musica"), nunca su valor entero: allowIntegerValues:false hace que un entero o un nombre
// inválido en un request body sea rechazado en la deserialización, lo que ASP.NET Core traduce
// automáticamente en un ModelState inválido -> el mismo contrato uniforme de error
// (VALIDATION_ERROR) configurado más abajo, sin ningún cambio en los controllers.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    });

// Los errores automáticos de ModelState (DataAnnotations/binding) siguen el mismo contrato
// público de error que ExceptionMiddleware (docs/api-mvp-plan.md §5): Code="VALIDATION_ERROR",
// TraceId, y Errors con los mensajes por campo. Nunca el ProblemDetails por defecto de ASP.NET.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value != null && kvp.Value.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        return new BadRequestObjectResult(new
        {
            code = "VALIDATION_ERROR",
            message = "Uno o más campos no son válidos.",
            traceId = context.HttpContext.Items["RequestId"]?.ToString() ?? context.HttpContext.TraceIdentifier,
            errors,
        });
    };
});

// Configuración de Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HoyDonde API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Ejemplo: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// Registrar dependencias para Repositorios y Servicios
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IIdentityProvider, FirebaseIdentityProvider>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ITicketValidationStore, FirestoreTicketValidationStore>();
builder.Services.AddScoped<ITicketService, TicketService>();

// Etapa 2 del refactor de seguridad (docs/security-refactor-plan.md §6)
builder.Services.AddScoped<IRolRepository, FirestoreRolRepository>();
builder.Services.AddScoped<IAccionRepository, FirestoreAccionRepository>();
builder.Services.AddScoped<IUsuarioRepository, FirestoreUsuarioRepository>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<SecurityCatalogSeeder>();

// Etapa 3 del refactor de seguridad (docs/security-refactor-plan.md §7)
builder.Services.AddScoped<IIdentidadHuerfanaRepository, FirestoreIdentidadHuerfanaRepository>();
builder.Services.AddScoped<BootstrapAdminCommand>();

// Etapa 4 del refactor de seguridad (docs/security-refactor-plan.md §4): resolución UID ->
// PersonaId reutilizada por EventService/TicketService/UserService, y asignación Control<->Evento.
builder.Services.AddScoped<IAuthenticatedPersonaResolver, AuthenticatedPersonaResolver>();
builder.Services.AddScoped<IControlAsignacionRepository, FirestoreControlAsignacionRepository>();

// Etapa 5 del refactor de seguridad (docs/security-refactor-plan.md §6): administración
// configurable de roles/acciones/usuarios, con el guard transaccional del último Administrador.
builder.Services.AddScoped<ISecurityAdminService, SecurityAdminService>();

// Módulo de reportes (docs/api-mvp-plan.md §11): reporte de solo lectura de eventos propios del
// Organizador, y el comando dedicado que crea únicamente las dos Acciones nuevas contra un
// Firestore real ya existente.
builder.Services.AddScoped<IReporteService, ReporteService>();
builder.Services.AddScoped<SeedReportActionsCommand>();

// Baja física de roles (docs/api-mvp-plan.md §12): comando dedicado que crea únicamente la
// Accion ROL_ELIMINAR contra un Firestore real ya existente.
builder.Services.AddScoped<SeedRoleDeletionActionCommand>();

// Recuperación de contraseña asistida por Administrador (docs/api-mvp-plan.md §13): comando
// dedicado que crea únicamente la Accion USUARIO_RESTABLECER_PASSWORD contra un Firestore real
// ya existente.
builder.Services.AddScoped<SeedPasswordResetActionCommand>();

// Reporte Admin de eventos globales y auditoría de seguridad (docs/api-mvp-plan.md §11.3, pasos
// 3-4): reutilizan ReporteFiltroValidator/ReporteMetricasCalculator (eventos) y agregan la lectura
// de solo lectura de security_audits.
builder.Services.AddScoped<ISecurityAuditRepository, FirestoreSecurityAuditRepository>();
builder.Services.AddScoped<ISecurityAuditReportService, SecurityAuditReportService>();


var app = builder.Build();

// Comando explícito de bootstrap del primer Administrador (docs/security-refactor-plan.md §5,
// Etapa 3): "dotnet run --project HoyDonde.API -- bootstrap-admin <email>". No es un endpoint
// HTTP y el proceso termina acá mismo, sin levantar el servidor. WebApplicationFactory (tests)
// invoca este entry point sin argumentos, así que esta rama nunca se activa en la suite de
// pruebas de integración.
if (args.Length > 0 && string.Equals(args[0], "bootstrap-admin", StringComparison.OrdinalIgnoreCase))
{
    using (var scope = app.Services.CreateScope())
    {
        var command = scope.ServiceProvider.GetRequiredService<BootstrapAdminCommand>();
        Environment.ExitCode = await command.RunAsync(args);
    }
    Log.CloseAndFlush();
    return;
}

// Comando dedicado del módulo de reportes (docs/api-mvp-plan.md §11.5):
// "dotnet run --project HoyDonde.API -- seed-report-actions". Igual criterio que bootstrap-admin:
// no es un endpoint HTTP, no levanta el servidor. Crea únicamente las dos Acciones nuevas
// (REPORTE_VER_GLOBAL/REPORTE_VER_PROPIO) contra un Firestore real que ya tiene el catálogo de
// seguridad instalado; nunca crea/edita roles ni asigna acciones a roles.
if (args.Length > 0 && string.Equals(args[0], "seed-report-actions", StringComparison.OrdinalIgnoreCase))
{
    using (var scope = app.Services.CreateScope())
    {
        var command = scope.ServiceProvider.GetRequiredService<SeedReportActionsCommand>();
        Environment.ExitCode = await command.RunAsync();
    }
    Log.CloseAndFlush();
    return;
}

// Comando dedicado de la baja física de roles (docs/api-mvp-plan.md §12):
// "dotnet run --project HoyDonde.API -- seed-role-deletion-action". Igual criterio que
// seed-report-actions: no es un endpoint HTTP, no levanta el servidor. Crea únicamente la
// Accion ROL_ELIMINAR; nunca crea/edita roles ni asigna acciones a roles.
if (args.Length > 0 && string.Equals(args[0], "seed-role-deletion-action", StringComparison.OrdinalIgnoreCase))
{
    using (var scope = app.Services.CreateScope())
    {
        var command = scope.ServiceProvider.GetRequiredService<SeedRoleDeletionActionCommand>();
        Environment.ExitCode = await command.RunAsync();
    }
    Log.CloseAndFlush();
    return;
}

// Comando dedicado de la recuperación de contraseña (docs/api-mvp-plan.md §13):
// "dotnet run --project HoyDonde.API -- seed-password-reset-action". Igual criterio que
// seed-report-actions/seed-role-deletion-action: no es un endpoint HTTP, no levanta el servidor.
// Crea únicamente la Accion USUARIO_RESTABLECER_PASSWORD; nunca crea/edita roles ni asigna
// acciones a roles.
if (args.Length > 0 && string.Equals(args[0], "seed-password-reset-action", StringComparison.OrdinalIgnoreCase))
{
    using (var scope = app.Services.CreateScope())
    {
        var command = scope.ServiceProvider.GetRequiredService<SeedPasswordResetActionCommand>();
        Environment.ExitCode = await command.RunAsync();
    }
    Log.CloseAndFlush();
    return;
}

// Uso de middlewares personalizados
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();

// Configuración de Swagger en ambiente de desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HoyDonde API v1");
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Seeding de Roles
// Role assignment is now handled by custom logic or Firebase claims management
/*
using (var scope = app.Services.CreateScope())
{
    // Roles seeding removed as Firebase handles usage
}
*/

// Iniciar la aplicación y manejo de excepciones en el arranque
Log.Information("Iniciando HoyDonde API");
try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación se detuvo inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
