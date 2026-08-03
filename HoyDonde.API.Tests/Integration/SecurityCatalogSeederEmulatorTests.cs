using System.Threading.Tasks;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Etapa 1 del refactor de seguridad (docs/security-refactor-plan.md §14, criterio de
    // finalización): "seed idempotente del catálogo base verificado en emulador", ejercitado
    // acá contra los repositorios reales, no mockeados.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class SecurityCatalogSeederEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public SecurityCatalogSeederEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        [FirestoreEmulatorFact]
        public async Task SeedAsync_RunTwice_DoesNotThrow_AndCatalogIsComplete()
        {
            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);
            var seeder = new SecurityCatalogSeeder(rolRepository, accionRepository);

            await seeder.SeedAsync();
            var ex = await Record.ExceptionAsync(() => seeder.SeedAsync());

            Assert.Null(ex);

            var control = await rolRepository.GetByCodigoAsync("CONTROL");
            Assert.NotNull(control);

            var accionesControl = await rolRepository.GetAccionCodigosAsync("CONTROL");
            Assert.Contains("TICKET_VALIDAR", accionesControl);

            var accionesAdmin = await rolRepository.GetAccionCodigosAsync("ADMINISTRADOR");
            Assert.Contains("USUARIO_ASIGNAR_ROL", accionesAdmin);

            // Módulo de reportes (docs/api-mvp-plan.md §11.5): instalaciones nuevas quedan con
            // ADMINISTRADOR->REPORTE_VER_GLOBAL y ORGANIZADOR->REPORTE_VER_PROPIO.
            Assert.Contains("REPORTE_VER_GLOBAL", accionesAdmin);
            var accionesOrganizador = await rolRepository.GetAccionCodigosAsync("ORGANIZADOR");
            Assert.Contains("REPORTE_VER_PROPIO", accionesOrganizador);
        }
    }
}
