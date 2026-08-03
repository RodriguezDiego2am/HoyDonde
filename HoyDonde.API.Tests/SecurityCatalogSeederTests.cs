using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Etapa 1 del refactor de seguridad (docs/security-refactor-plan.md §8, capa 1): la
    // lógica de siembra se prueba con repositorios mockeados. La persistencia real contra
    // Firestore se prueba en HoyDonde.API.Tests/Integration (Firestore Emulator).
    public class SecurityCatalogSeederTests
    {
        private static (SecurityCatalogSeeder sut, Mock<IRolRepository> rolRepository, Mock<IAccionRepository> accionRepository) CreateSut()
        {
            var rolRepository = new Mock<IRolRepository>();
            var accionRepository = new Mock<IAccionRepository>();
            var sut = new SecurityCatalogSeeder(rolRepository.Object, accionRepository.Object);
            return (sut, rolRepository, accionRepository);
        }

        [Fact]
        public async Task SeedAsync_CreatesAllRolesAndAcciones_AndAssignsAccionesPorRol()
        {
            var (sut, rolRepository, accionRepository) = CreateSut();

            await sut.SeedAsync();

            rolRepository.Verify(r => r.CreateAsync(It.Is<Rol>(x => x.Codigo == "ADMINISTRADOR")), Times.Once);
            rolRepository.Verify(r => r.CreateAsync(It.Is<Rol>(x => x.Codigo == "CLIENTE")), Times.Once);
            rolRepository.Verify(r => r.CreateAsync(It.Is<Rol>(x => x.Codigo == "ORGANIZADOR")), Times.Once);
            rolRepository.Verify(r => r.CreateAsync(It.Is<Rol>(x => x.Codigo == "CONTROL")), Times.Once);

            accionRepository.Verify(r => r.CreateAsync(It.Is<Accion>(x => x.Codigo == "TICKET_VALIDAR")), Times.Once);
            accionRepository.Verify(r => r.CreateAsync(It.Is<Accion>(x => x.Codigo == "ROL_CREAR")), Times.Once);
            // Módulo de reportes (docs/api-mvp-plan.md §11.5): 20 -> 22 acciones para
            // instalaciones nuevas.
            accionRepository.Verify(r => r.CreateAsync(It.Is<Accion>(x => x.Codigo == "REPORTE_VER_GLOBAL")), Times.Once);
            accionRepository.Verify(r => r.CreateAsync(It.Is<Accion>(x => x.Codigo == "REPORTE_VER_PROPIO")), Times.Once);

            rolRepository.Verify(r => r.AssignAccionAsync("CONTROL", "TICKET_VALIDAR", SecurityCatalogSeeder.SeedActor), Times.Once);
            rolRepository.Verify(r => r.AssignAccionAsync("CLIENTE", "TICKET_COMPRAR", SecurityCatalogSeeder.SeedActor), Times.Once);
            rolRepository.Verify(r => r.AssignAccionAsync("ORGANIZADOR", "EVENTO_CREAR", SecurityCatalogSeeder.SeedActor), Times.Once);
            rolRepository.Verify(r => r.AssignAccionAsync("ADMINISTRADOR", "USUARIO_DESACTIVAR", SecurityCatalogSeeder.SeedActor), Times.Once);
            rolRepository.Verify(r => r.AssignAccionAsync("ADMINISTRADOR", "REPORTE_VER_GLOBAL", SecurityCatalogSeeder.SeedActor), Times.Once);
            rolRepository.Verify(r => r.AssignAccionAsync("ORGANIZADOR", "REPORTE_VER_PROPIO", SecurityCatalogSeeder.SeedActor), Times.Once);
            // Las otras 20 acciones nunca reciben una asignación de reporte: ORGANIZADOR jamás ve
            // REPORTE_VER_GLOBAL, ni ADMINISTRADOR ve REPORTE_VER_PROPIO.
            rolRepository.Verify(r => r.AssignAccionAsync("ORGANIZADOR", "REPORTE_VER_GLOBAL", It.IsAny<string>()), Times.Never);
            rolRepository.Verify(r => r.AssignAccionAsync("ADMINISTRADOR", "REPORTE_VER_PROPIO", It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SeedAsync_IsIdempotent_WhenRolesAndAccionesAlreadyExist()
        {
            var (sut, rolRepository, accionRepository) = CreateSut();
            rolRepository
                .Setup(r => r.CreateAsync(It.IsAny<Rol>()))
                .ThrowsAsync(new RolYaExisteException("ya-existe"));
            accionRepository
                .Setup(r => r.CreateAsync(It.IsAny<Accion>()))
                .ThrowsAsync(new AccionYaExisteException("ya-existe"));

            var ex = await Record.ExceptionAsync(() => sut.SeedAsync());

            Assert.Null(ex);
            // Las asignaciones Rol->Accion son Set idempotentes: se siguen aplicando aunque
            // el Rol/Accion ya existieran de una corrida anterior.
            rolRepository.Verify(r => r.AssignAccionAsync("CLIENTE", "TICKET_COMPRAR", SecurityCatalogSeeder.SeedActor), Times.Once);
        }
    }
}
