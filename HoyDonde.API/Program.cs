using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using HoyDonde.API.Authorization;
using HoyDonde.API.Commands;
using HoyDonde.API.Middleware;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
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

// Configuración de Firebase
var firebaseCredentialsPath = builder.Configuration["Firebase:CredentialsPath"] ?? "firebase-service-account.json";
if (File.Exists(firebaseCredentialsPath))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(firebaseCredentialsPath)
    });
}
else
{
    Log.Warning("Firebase credentials file not found at {Path}. App may fail to authenticate.", firebaseCredentialsPath);
}

// Configuración de Firestore
builder.Services.AddSingleton<FirestoreDb>(provider =>
{
    var projectId = builder.Configuration["Firebase:ProjectId"];
    if (string.IsNullOrEmpty(projectId)) throw new Exception("Firebase ProjectId is missing in configuration.");
    return FirestoreDb.Create(projectId);
});

// Configuración de autenticación con Firebase JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    var projectId = builder.Configuration["Firebase:ProjectId"];
    options.Authority = $"https://securetoken.google.com/{projectId}";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = $"https://securetoken.google.com/{projectId}",
        ValidateAudience = true,
        ValidAudience = projectId,
        ValidateLifetime = true,
    };
});

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
