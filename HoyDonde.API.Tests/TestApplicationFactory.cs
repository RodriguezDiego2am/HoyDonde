using System;
using System.Collections.Generic;
using System.Linq;
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

            // Etapa 5 del refactor de seguridad: la autorización real depende exclusivamente de
            // IPermissionService resuelto a partir del UID (ver AccionAuthorizationHandler), así
            // que la identidad falsa solo aporta los claims necesarios para autenticación (UID y
            // email). "Test-Uid" permite a un test simular un actor SIN ninguna acción concedida
            // (uid nunca pasado a TestApplicationFactory.GrantAccion) sin tener que mutar el
            // estado compartido de los mocks de permisos entre tests.
            var uid = Request.Headers.ContainsKey("Test-Uid")
                ? Request.Headers["Test-Uid"].ToString()
                : "test-uid-123";

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, uid),
                new Claim(ClaimTypes.Email, "test@example.com"),
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
        public Mock<ITicketService> MockTicketService { get; } = new();
        public Mock<IUsuarioRepository> MockUsuarioRepository { get; } = new();
        public Mock<IRolRepository> MockRolRepository { get; } = new();
        public Mock<IAccionRepository> MockAccionRepository { get; } = new();
        public Mock<ISecurityAdminService> MockSecurityAdminService { get; } = new();
        public Mock<IReporteService> MockReporteService { get; } = new();
        public Mock<ISecurityAuditReportService> MockSecurityAuditReportService { get; } = new();
        public Mock<IVentasReporteService> MockVentasReporteService { get; } = new();

        // Etapa 5 del refactor de seguridad: concede, para el uid dado, exactamente los
        // accionCodigos indicados (via un único rol de prueba), dejando MockUsuarioRepository/
        // MockRolRepository/MockAccionRepository listos para que AccionAuthorizationHandler
        // resuelva "autorizado" a través de la implementación real de IPermissionService (que
        // sigue registrada tal cual en el contenedor de test, sin mockear). Pensado para usarse
        // una vez por uid (constructor de la clase de test); llamar dos veces con el MISMO uid
        // sobrescribe la asignación de rol anterior.
        public void GrantAccion(string uid, string usuarioId, string personaId, params string[] accionCodigos)
        {
            const string rolCodigo = "ROL_TEST";

            MockUsuarioRepository
                .Setup(r => r.GetUsuarioIdByExternalSubjectAsync("FIREBASE", uid))
                .ReturnsAsync(usuarioId);
            MockUsuarioRepository
                .Setup(r => r.GetByIdAsync(usuarioId))
                .ReturnsAsync(new Models.Usuario { Id = usuarioId, PersonaId = personaId, IsActive = true });
            MockUsuarioRepository
                .Setup(r => r.GetRolCodigosActivosAsync(usuarioId))
                .ReturnsAsync(new List<string> { rolCodigo });

            MockRolRepository
                .Setup(r => r.GetByCodigoAsync(rolCodigo))
                .ReturnsAsync(new Models.Rol { Codigo = rolCodigo, Nombre = "Rol de prueba", Activo = true });
            MockRolRepository
                .Setup(r => r.GetAccionCodigosAsync(rolCodigo))
                .ReturnsAsync(accionCodigos.ToList());

            foreach (var accionCodigo in accionCodigos)
            {
                MockAccionRepository
                    .Setup(r => r.GetByCodigoAsync(accionCodigo))
                    .ReturnsAsync(new Models.Accion { Codigo = accionCodigo, Activo = true });
            }
        }

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

                // Etapa 5 del refactor de seguridad: SecurityAdminController se prueba contra
                // MockSecurityAdminService directamente (mismo patrón que MockEventService), no
                // contra la implementación real -esa se cubre en tests de servicio/integración
                // separados-.
                var securityAdminServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISecurityAdminService));
                if (securityAdminServiceDescriptor != null) services.Remove(securityAdminServiceDescriptor);

                // Módulo de reportes: ReporteController se prueba contra MockReporteService
                // directamente, mismo patrón que MockEventService. La implementación real
                // (ReporteService, dependiente de FirestoreDb) se cubre en tests de servicio
                // (ReporteMetricasCalculatorTests/ReporteFiltroValidatorTests) y de integración
                // (Integration/ReporteServiceEmulatorTests).
                var reporteServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IReporteService));
                if (reporteServiceDescriptor != null) services.Remove(reporteServiceDescriptor);

                // Mismo criterio: reporte de ventas simuladas contra MockVentasReporteService.
                var ventasReporteServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IVentasReporteService));
                if (ventasReporteServiceDescriptor != null) services.Remove(ventasReporteServiceDescriptor);

                // Mismo criterio: ReportsController se prueba contra MockSecurityAuditReportService
                // directamente. ISecurityAuditRepository (dependencia real de
                // FirestoreSecurityAuditRepository -> FirestoreDb) también se retira: nada la
                // construye en estos tests, pero ValidateOnBuild igual necesita poder resolverla.
                var securityAuditReportServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISecurityAuditReportService));
                if (securityAuditReportServiceDescriptor != null) services.Remove(securityAuditReportServiceDescriptor);

                var securityAuditRepositoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISecurityAuditRepository));
                if (securityAuditRepositoryDescriptor != null) services.Remove(securityAuditRepositoryDescriptor);

                // Add Mocks
                services.AddSingleton(MockEventService.Object);
                services.AddSingleton(MockUserService.Object);
                services.AddSingleton(MockAuthService.Object);
                services.AddSingleton(MockTicketService.Object);
                services.AddSingleton(MockSecurityAdminService.Object);
                services.AddSingleton(MockReporteService.Object);
                services.AddSingleton(MockVentasReporteService.Object);
                services.AddSingleton(MockSecurityAuditReportService.Object);
                services.AddSingleton(Mock.Of<ISecurityAuditRepository>());
                services.AddSingleton(Mock.Of<ITicketValidationStore>());
                // Etapa 5: estos tres quedan como Mock<T> reales (no Mock.Of<T> anónimos) para que
                // GrantAccion pueda configurarlos por test y AccionAuthorizationHandler resuelva
                // autorización real contra la implementación real de IPermissionService (que sigue
                // registrada tal cual, sin mockear).
                services.AddSingleton(MockUsuarioRepository.Object);
                services.AddSingleton(MockRolRepository.Object);
                services.AddSingleton(MockAccionRepository.Object);
                services.AddSingleton(Mock.Of<IIdentidadHuerfanaRepository>());
                services.AddSingleton(Mock.Of<IControlAsignacionRepository>());

                // Add Fake Authentication
                services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>("Test", options => { });
            });
        }
    }
}
