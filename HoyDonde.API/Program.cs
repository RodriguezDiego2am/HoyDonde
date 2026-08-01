using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using HoyDonde.API.Commands;
using HoyDonde.API.Middleware;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;

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
        RoleClaimType = "role" // Maps Firebase custom claim 'role' to ClaimsIdentity.RoleClaimType
    };
});

// Agregar servicios de autorización
builder.Services.AddAuthorization();

// Agregar controladores
builder.Services.AddControllers();

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
builder.Services.AddScoped<IUserRepository, FirestoreUserRepository>();
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
