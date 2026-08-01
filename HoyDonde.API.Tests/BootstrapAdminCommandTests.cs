using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.Commands;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // docs/security-refactor-plan.md §5, Etapa 3, criterio E: bootstrap deshabilitado y
    // segundo Administrador rechazado se prueban acá con mocks (sin emulador). El camino
    // exitoso contra Firestore real vive en Integration/BootstrapAdminCommandEmulatorTests.cs.
    public class BootstrapAdminCommandTests
    {
        private static IConfiguration BuildConfiguration(bool allowBootstrap)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Bootstrap:AllowAdminBootstrap"] = allowBootstrap.ToString(),
                })
                .Build();
        }

        private static (
            BootstrapAdminCommand sut,
            Mock<IUsuarioRepository> usuarioRepository,
            Mock<IRolRepository> rolRepository,
            Mock<IUserService> userService) CreateSut(bool allowBootstrap)
        {
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var rolRepository = new Mock<IRolRepository>();
            var accionRepository = new Mock<IAccionRepository>();
            var userService = new Mock<IUserService>();

            rolRepository.Setup(r => r.CreateAsync(It.IsAny<Rol>())).Returns(Task.CompletedTask);
            rolRepository.Setup(r => r.AssignAccionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            accionRepository.Setup(a => a.CreateAsync(It.IsAny<Accion>())).Returns(Task.CompletedTask);

            var seeder = new SecurityCatalogSeeder(rolRepository.Object, accionRepository.Object);
            var sut = new BootstrapAdminCommand(
                BuildConfiguration(allowBootstrap),
                seeder,
                usuarioRepository.Object,
                rolRepository.Object,
                userService.Object,
                Mock.Of<ILogger<BootstrapAdminCommand>>());

            return (sut, usuarioRepository, rolRepository, userService);
        }

        [Fact]
        public async Task RunAsync_WhenBootstrapDisabled_ReturnsError_AndCreatesNothing()
        {
            var (sut, _, rolRepository, userService) = CreateSut(allowBootstrap: false);

            var exitCode = await sut.RunAsync(new[] { "bootstrap-admin", "admin@test.com" });

            Assert.NotEqual(0, exitCode);
            rolRepository.Verify(r => r.CreateAsync(It.IsAny<Rol>()), Times.Never);
            userService.Verify(s => s.RegisterAdminAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RunAsync_WhenEffectiveAdministratorAlreadyExists_ReturnsError_AndDoesNotCreateAnother()
        {
            var (sut, usuarioRepository, rolRepository, userService) = CreateSut(allowBootstrap: true);

            rolRepository.Setup(r => r.GetByCodigoAsync("ADMINISTRADOR")).ReturnsAsync(new Rol { Codigo = "ADMINISTRADOR", Activo = true });
            usuarioRepository.Setup(r => r.GetUsuarioIdsConRolActivoAsync("ADMINISTRADOR")).ReturnsAsync(new List<string> { "usuario-existente" });
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-existente")).ReturnsAsync(new Usuario { Id = "usuario-existente", IsActive = true });

            var exitCode = await sut.RunAsync(new[] { "bootstrap-admin", "otro-admin@test.com" });

            Assert.NotEqual(0, exitCode);
            userService.Verify(s => s.RegisterAdminAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RunAsync_WhenEnabledAndNoEffectiveAdministrator_ProvisionsAdminWithBootstrapMarker()
        {
            var (sut, usuarioRepository, rolRepository, userService) = CreateSut(allowBootstrap: true);

            rolRepository.Setup(r => r.GetByCodigoAsync("ADMINISTRADOR")).ReturnsAsync(new Rol { Codigo = "ADMINISTRADOR", Activo = true });
            usuarioRepository.Setup(r => r.GetUsuarioIdsConRolActivoAsync("ADMINISTRADOR")).ReturnsAsync(new List<string>());
            userService
                .Setup(s => s.RegisterAdminAsync(BootstrapAdminCommand.BootstrapAssignedByMarker, "nuevo-admin@test.com", "Password123!"))
                .ReturnsAsync(new UsuarioProvisioningResult("persona-boot", "usuario-boot"));

            var exitCode = await sut.RunAsync(new[] { "bootstrap-admin", "nuevo-admin@test.com" }, passwordReader: () => "Password123!");

            Assert.Equal(0, exitCode);
            userService.Verify(s => s.RegisterAdminAsync(BootstrapAdminCommand.BootstrapAssignedByMarker, "nuevo-admin@test.com", "Password123!"), Times.Once);
        }

        [Fact]
        public async Task RunAsync_WithoutEmailArgument_ReturnsError()
        {
            var (sut, _, _, userService) = CreateSut(allowBootstrap: true);

            var exitCode = await sut.RunAsync(new[] { "bootstrap-admin" });

            Assert.NotEqual(0, exitCode);
            userService.Verify(s => s.RegisterAdminAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
