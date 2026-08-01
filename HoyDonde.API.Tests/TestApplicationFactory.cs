using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace HoyDonde.API.Tests
{
    public class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public FakeAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var role = Request.Headers.ContainsKey("Test-Role") 
                ? Request.Headers["Test-Role"].ToString() 
                : Models.Roles.Organizador;

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-uid-123"),
                new Claim(ClaimTypes.Email, "test@example.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role) // Firebase custom claim mock
            };

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public class TestApplicationFactory : WebApplicationFactory<Program>
    {
        public Mock<IEventService> MockEventService { get; } = new();
        public Mock<IUserService> MockUserService { get; } = new();
        public Mock<IAuthService> MockAuthService { get; } = new();
        public Mock<IUserRepository> MockUserRepository { get; } = new();
        public Mock<ITicketService> MockTicketService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            // Configure test environment variables or settings here
            Environment.SetEnvironmentVariable("Firebase__ProjectId", "test-project-123");
            
            builder.ConfigureServices(services =>
            {
                // Remove Firebase services and real repos
                var firestoreDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(FirestoreDb));
                if (firestoreDescriptor != null) services.Remove(firestoreDescriptor);

                var userRepoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IUserRepository));
                if (userRepoDescriptor != null) services.Remove(userRepoDescriptor);

                var eventServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEventService));
                if (eventServiceDescriptor != null) services.Remove(eventServiceDescriptor);

                var userServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IUserService));
                if (userServiceDescriptor != null) services.Remove(userServiceDescriptor);

                var authServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAuthService));
                if (authServiceDescriptor != null) services.Remove(authServiceDescriptor);

                var ticketServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITicketService));
                if (ticketServiceDescriptor != null) services.Remove(ticketServiceDescriptor);

                // TicketService (real impl) depends on ITicketValidationStore -> FirestoreDb, which
                // we just removed above. ITicketService itself is fully mocked below, so the real
                // TicketService is never constructed in these tests, but the Development host still
                // validates the whole DI graph on build (ValidateOnBuild), so this registration must
                // still resolve. Swap it for a mock nobody calls.
                var ticketValidationStoreDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITicketValidationStore));
                if (ticketValidationStoreDescriptor != null) services.Remove(ticketValidationStoreDescriptor);

                // Etapa 2 del refactor de seguridad: IUsuarioRepository/IRolRepository/
                // IAccionRepository también dependen de FirestoreDb. Nada en IUserService
                // (mockeado abajo) llega a construir las implementaciones reales en estos tests,
                // pero ValidateOnBuild igual intentaría resolverlas si quedaran registradas tal
                // cual. IPermissionService no necesita este tratamiento: sus 3 dependencias son
                // interfaces que ya quedan mockeadas, así que la implementación real se puede
                // construir sin tocar Firestore.
                var usuarioRepositoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IUsuarioRepository));
                if (usuarioRepositoryDescriptor != null) services.Remove(usuarioRepositoryDescriptor);

                var rolRepositoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRolRepository));
                if (rolRepositoryDescriptor != null) services.Remove(rolRepositoryDescriptor);

                var accionRepositoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAccionRepository));
                if (accionRepositoryDescriptor != null) services.Remove(accionRepositoryDescriptor);

                // Etapa 3 del refactor de seguridad: IIdentidadHuerfanaRepository también
                // depende de FirestoreDb. IUserService/IAuthService quedan mockeados abajo, así
                // que nada llega a construir FirestoreIdentidadHuerfanaRepository en estos tests,
                // pero ValidateOnBuild igual necesita poder resolverlo.
                var identidadHuerfanaRepositoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IIdentidadHuerfanaRepository));
                if (identidadHuerfanaRepositoryDescriptor != null) services.Remove(identidadHuerfanaRepositoryDescriptor);

                // Etapa 4 del refactor de seguridad: IControlAsignacionRepository también
                // depende de FirestoreDb. TicketService/UserService quedan mockeados o resueltos
                // contra este mock, pero ValidateOnBuild igual necesita poder resolverlo.
                var controlAsignacionRepositoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IControlAsignacionRepository));
                if (controlAsignacionRepositoryDescriptor != null) services.Remove(controlAsignacionRepositoryDescriptor);

                // Add Mocks
                services.AddSingleton(MockEventService.Object);
                services.AddSingleton(MockUserService.Object);
                services.AddSingleton(MockAuthService.Object);
                services.AddSingleton(MockUserRepository.Object);
                services.AddSingleton(MockTicketService.Object);
                services.AddSingleton(Mock.Of<ITicketValidationStore>());
                services.AddSingleton(Mock.Of<IUsuarioRepository>());
                services.AddSingleton(Mock.Of<IRolRepository>());
                services.AddSingleton(Mock.Of<IAccionRepository>());
                services.AddSingleton(Mock.Of<IIdentidadHuerfanaRepository>());
                services.AddSingleton(Mock.Of<IControlAsignacionRepository>());

                // Add Fake Authentication
                services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>("Test", options => { });
            });
        }
    }
}
