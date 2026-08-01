using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Ejercita AuthService.SyncClienteAsync directamente (sin HTTP): flujo Cliente de
    // docs/security-refactor-plan.md §2.1, Etapa 3. La idempotencia real vive en
    // IUsuarioRepository.ProvisionarAsync (Etapa 2, ya probada contra el emulador); acá se
    // ejercita el comportamiento de AuthService alrededor de esa llamada: la ausencia total de
    // compensación.
    public class AuthServiceSyncTests
    {
        private const string Uid = "uid-cliente-1";
        private const string Email = "cliente@test.com";

        private static (AuthService sut, Mock<IUsuarioRepository> usuarioRepository) CreateSut()
        {
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var sut = new AuthService(usuarioRepository.Object);
            return (sut, usuarioRepository);
        }

        [Fact]
        public async Task SyncClienteAsync_NewUser_ProvisionsClienteOnly_WithSelfRegistrationActor()
        {
            var (sut, usuarioRepository) = CreateSut();

            UsuarioProvisioningRequest? capturedRequest = null;
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .Callback<UsuarioProvisioningRequest>(r => capturedRequest = r)
                .ReturnsAsync(new UsuarioProvisioningResult("persona-1", "usuario-1"));
            usuarioRepository
                .Setup(r => r.GetRolCodigosActivosAsync("usuario-1"))
                .ReturnsAsync(new List<string> { "CLIENTE" });

            var result = await sut.SyncClienteAsync(Uid, Email, new SyncClienteRequest("Juan Perez", "12345678", "+5491122334455"));

            Assert.Equal("usuario-1", result.UsuarioId);
            Assert.Equal("persona-1", result.PersonaId);
            Assert.Contains("CLIENTE", result.Roles);

            Assert.NotNull(capturedRequest);
            Assert.Equal("CLIENTE", capturedRequest!.RolCodigo);
            Assert.Equal(UserService.SelfRegistrationActor, capturedRequest.AssignedBy);
            Assert.Equal(Uid, capturedRequest.ExternalSubjectId);
            Assert.Equal(Email, capturedRequest.Email);
            Assert.Equal(FirebaseIdentityProvider.ProviderName, capturedRequest.IdentityProvider);
        }

        [Fact]
        public async Task SyncClienteAsync_ExistingNonClienteUser_KeepsExistingRoles()
        {
            var (sut, usuarioRepository) = CreateSut();

            // La idempotencia real la resuelve ProvisionarAsync (Etapa 2): para un Usuario ya
            // existente devuelve sus IDs originales sin tocar sus roles. Acá se simula ese
            // resultado para un Usuario cuyo único rol real es ADMINISTRADOR.
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .ReturnsAsync(new UsuarioProvisioningResult("persona-admin", "usuario-admin"));
            usuarioRepository
                .Setup(r => r.GetRolCodigosActivosAsync("usuario-admin"))
                .ReturnsAsync(new List<string> { "ADMINISTRADOR" });

            var result = await sut.SyncClienteAsync(Uid, Email, new SyncClienteRequest(null, null, null));

            Assert.DoesNotContain("CLIENTE", result.Roles);
            Assert.Contains("ADMINISTRADOR", result.Roles);
        }

        [Fact]
        public async Task SyncClienteAsync_WhenProvisioningFails_Propagates()
        {
            var (sut, usuarioRepository) = CreateSut();
            var firestoreError = new InvalidOperationException("firestore failed");
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .ThrowsAsync(firestoreError);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.SyncClienteAsync(Uid, Email, new SyncClienteRequest(null, null, null)));

            Assert.Same(firestoreError, thrown);
        }
    }
}
