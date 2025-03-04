using HoyDonde.API.Data;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1) Configurar la conexión a SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2) Configurar servicios de Swagger
builder.Services.AddEndpointsApiExplorer();

// ─────────────────────────────────────────────────────────────────────────────
// Agregamos el esquema de seguridad en Swagger para que podamos enviar el token
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HoyDonde API", Version = "v1" });

    // Para que Swagger pueda usar JWT
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
            System.Array.Empty<string>()
        }
    });
});

// 3) Configurar Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ─────────────────────────────────────────────────────────────────────────────
// 4) Configurar la autenticación con JWT
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    // Leemos claves del appsettings.json (JwtSettings:Secret, etc.)
    var secretKey = builder.Configuration["JwtSettings:Secret"] ?? "";
    var issuer = builder.Configuration["JwtSettings:Issuer"] ?? "";
    var audience = builder.Configuration["JwtSettings:Audience"] ?? "";

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = System.TimeSpan.Zero
    };
});

// 5) Registrar los Repositorios con UnitOfWork
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();


// 6) Registrar los Servicios
builder.Services.AddScoped<AuthService>();

// ─────────────────────────────────────────────────────────────────────────────
// 7) (OPCIONAL) Registrar un servicio para generar el JWT si no lo tienes aún.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IJwtService, JwtService>();

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// 8) Swagger en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HoyDonde API v1");
    });
}

// 9) Activar la autenticación y autorización
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
