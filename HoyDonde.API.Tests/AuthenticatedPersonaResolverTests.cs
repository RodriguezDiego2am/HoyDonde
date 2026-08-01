using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Resolución UID (ExternalSubjectId de Firebase) -> Usuario -> PersonaId
    // (docs/security-refactor-plan.md §1/§4, Etapa 4), incluyendo el comportamiento explícito
    // para un token válido sin identidad aprovisionada en el modelo nuevo.
    public class AuthenticatedPersonaResolverTests
    {
        private const string ExternalSubjectId = "uid-123";

        private static (AuthenticatedPersonaResolver sut, Mock<IUsuarioRepository> usuarioRepository) CreateSut()
        {
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var sut = new AuthenticatedPersonaResolver(usuarioRepository.Object);
            return (sut, usuarioRepository);
        }

        [Fact]
        public async Task ResolvePersonaIdAsync_KnownExternalSubjectId_ReturnsPersonaId()
        {
            var (sut, usuarioRepository) = CreateSut();
            usuarioRepository
                .Setup(r => r.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, ExternalSubjectId))
                .ReturnsAsync("usuario-1");
            usuarioRepository
                .Setup(r => r.GetByIdAsync("usuario-1"))
                .ReturnsAsync(new Usuario { Id = "usuario-1", PersonaId = "persona-1" });

            var personaId = await sut.ResolvePersonaIdAsync(ExternalSubjectId);

            Assert.Equal("persona-1", personaId);
        }

        [Fact]
        public async Task ResolvePersonaIdAsync_NoIdentidadExterna_ThrowsIdentityNotProvisionedException()
        {
            var (sut, usuarioRepository) = CreateSut();
            usuarioRepository
                .Setup(r => r.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, ExternalSubjectId))
                .ReturnsAsync((string?)null);

            var ex = await Assert.ThrowsAsync<IdentityNotProvisionedException>(() => sut.ResolvePersonaIdAsync(ExternalSubjectId));

            // El UID se conserva como propiedad para logging interno, pero el mensaje público
            // (lo que eventualmente devuelve el middleware/controller) nunca debe incluirlo.
            Assert.Equal(ExternalSubjectId, ex.ExternalSubjectId);
            Assert.DoesNotContain(ExternalSubjectId, ex.Message);
        }

        [Fact]
        public async Task ResolvePersonaIdAsync_IdentidadExternaApuntaAUsuarioInexistente_ThrowsIdentityNotProvisionedException()
        {
            var (sut, usuarioRepository) = CreateSut();
            usuarioRepository
                .Setup(r => r.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, ExternalSubjectId))
                .ReturnsAsync("usuario-fantasma");
            usuarioRepository
                .Setup(r => r.GetByIdAsync("usuario-fantasma"))
                .ReturnsAsync((Usuario?)null);

            await Assert.ThrowsAsync<IdentityNotProvisionedException>(() => sut.ResolvePersonaIdAsync(ExternalSubjectId));
        }
    }
}
