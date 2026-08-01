using System;
using System.Threading.Tasks;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Etapa 2 del refactor de seguridad (docs/security-refactor-plan.md §14, criterio de
    // finalización): "PermissionService responde correctamente sin caché", ejercitado acá
    // contra el catálogo real de Rol/Accion sembrado por SecurityCatalogSeeder (Etapa 1).
    [Collection(FirestoreEmulatorCollection.Name)]
    public class PermissionServiceEmulatorTests
    {
        private const string Provider = "FIREBASE";
        private readonly FirestoreEmulatorFixture _fixture;

        public PermissionServiceEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        [FirestoreEmulatorFact]
        public async Task TieneAccionAsync_RespondeSegunElCatalogoReal_SinCache()
        {
            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            await new SecurityCatalogSeeder(rolRepository, accionRepository).SeedAsync();

            var externalSubjectId = $"uid-{Guid.NewGuid():N}";
            await usuarioRepository.ProvisionarAsync(new UsuarioProvisioningRequest(
                $"persona-{Guid.NewGuid():N}", $"usuario-{Guid.NewGuid():N}",
                Provider, externalSubjectId, "control@test.com", "CONTROL", "test"));

            var sut = new PermissionService(usuarioRepository, rolRepository, accionRepository);

            Assert.True(await sut.TieneAccionAsync(Provider, externalSubjectId, "TICKET_VALIDAR"));
            Assert.False(await sut.TieneAccionAsync(Provider, externalSubjectId, "EVENTO_CREAR"));
            Assert.False(await sut.TieneAccionAsync(Provider, externalSubjectId, "ACCION_QUE_NO_EXISTE"));
        }
    }
}
