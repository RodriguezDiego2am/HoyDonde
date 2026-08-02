using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoyDonde.API.Tests.Integration
{
    // A diferencia de TestApplicationFactory (que mockea IEventService/ITicketService/
    // IUserService por completo), esta variante deja los servicios y repositorios REALES
    // (EventService, TicketService, UserService, PermissionService, FirestoreUsuarioRepository,
    // etc.) tal como los registra Program.cs, y solo reemplaza el FirestoreDb real por uno que
    // apunta al Firestore Emulator (docs/api-mvp-plan.md §5: "pipeline HTTP real; controllers
    // reales; services y repositorios reales; Firestore Emulator"). Lo único sustituido para
    // simular autenticación es el esquema JWT de Firebase por un esquema "Test" que arma los
    // claims (UID/email) a partir del header Test-Uid -mismo criterio que FakeAuthHandler en
    // TestApplicationFactory, pero sin un uid por defecto: acá cada actor del recorrido
    // (organizador/cliente/control) necesita su propio UID explícito.
    public class EmulatorWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly FirestoreDb _db;

        public EmulatorWebApplicationFactory(FirestoreDb db)
        {
            _db = db;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("Firebase__ProjectId", "hoydonde-security-refactor-tests");

            builder.ConfigureServices(services =>
            {
                var firestoreDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(FirestoreDb));
                if (firestoreDescriptor != null) services.Remove(firestoreDescriptor);
                services.AddSingleton(_db);

                services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, EmulatorFakeAuthHandler>("Test", options => { });
            });
        }
    }

    // Igual criterio que FakeAuthHandler (TestApplicationFactory.cs): la identidad falsa solo
    // aporta UID y email como claims. Toda decisión de autorización real sigue dependiendo de
    // IPermissionService resuelto contra Firestore (AccionAuthorizationHandler), nunca de un
    // claim de rol. Sin Test-Uid, la request queda anónima (NoResult), igual que sin header
    // Authorization en el esquema JWT real.
    public class EmulatorFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public EmulatorFakeAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Test-Uid"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var uid = Request.Headers["Test-Uid"].ToString();
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, uid),
                new Claim(ClaimTypes.Email, $"{uid}@test.hoydonde.local"),
            };

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
