using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Flujo Cliente de POST /api/auth/sync contra Firestore Emulator real
    // (docs/security-refactor-plan.md §2.1, Etapa 3, criterio de finalización). El proveedor de
    // identidad se mockea (acá no se ejercita Firebase Auth, solo el aprovisionamiento en
    // Firestore vía IUsuarioRepository real).
    [Collection(FirestoreEmulatorCollection.Name)]
    public class AuthServiceSyncEmulatorTests
    {
        private const string Provider = "FIREBASE";
        private readonly FirestoreEmulatorFixture _fixture;

        public AuthServiceSyncEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        [FirestoreEmulatorFact]
        public async Task SyncClienteAsync_SecondCall_IsIdempotent_DoesNotDuplicateNorChangeRole()
        {
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var identityProvider = new Mock<IIdentityProvider>();
            identityProvider
                .Setup(p => p.SetTemporaryClaimAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
                .Returns(Task.CompletedTask);

            var sut = new AuthService(usuarioRepository, identityProvider.Object);

            var uid = $"uid-{Guid.NewGuid():N}";
            var email = "cliente-sync@test.com";

            var primero = await sut.SyncClienteAsync(uid, email, new SyncClienteRequest("Juan Perez", "12345678", "+5491100000000"));
            Assert.Contains("CLIENTE", primero.Roles);
            Assert.True(primero.ClaimsUpdated);

            // Segundo sync con datos de body distintos: no debe duplicar Persona/Usuario ni
            // cambiar de rol -los datos de body se ignoran por completo en el camino idempotente-.
            var segundo = await sut.SyncClienteAsync(uid, email, new SyncClienteRequest("Otro Nombre", "99999999", "+5499999999"));

            Assert.Equal(primero.UsuarioId, segundo.UsuarioId);
            Assert.Equal(primero.PersonaId, segundo.PersonaId);
            Assert.Equal(new List<string> { "CLIENTE" }, segundo.Roles);
            Assert.True(segundo.ClaimsUpdated);

            var usuarioIdResuelto = await usuarioRepository.GetUsuarioIdByExternalSubjectAsync(Provider, uid);
            Assert.Equal(primero.UsuarioId, usuarioIdResuelto);

            var personaSnapshot = await _fixture.Db!.Collection("personas").Document(primero.PersonaId).GetSnapshotAsync();
            Assert.True(personaSnapshot.Exists);
            Assert.Equal("Juan Perez", personaSnapshot.ConvertTo<Models.Persona>().FullName);

            identityProvider.Verify(p => p.SetTemporaryClaimAsync(uid, It.IsAny<IReadOnlyDictionary<string, object>>()), Times.Exactly(2));
            identityProvider.Verify(p => p.DeleteIdentityAsync(It.IsAny<string>()), Times.Never);
        }

        [FirestoreEmulatorFact]
        public async Task SyncClienteAsync_ForUsuarioWithNonClienteRole_NeverAddsClienteRole()
        {
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var identityProvider = new Mock<IIdentityProvider>();
            identityProvider
                .Setup(p => p.SetTemporaryClaimAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
                .Returns(Task.CompletedTask);

            var uid = $"uid-{Guid.NewGuid():N}";

            // Simula un Usuario ya provisionado con otro rol (p. ej. Organizador, por
            // UserController) antes de que ese mismo uid llame a /api/auth/sync. Se usa
            // ORGANIZADOR y no ADMINISTRADOR a propósito: BootstrapAdminCommandEmulatorTests
            // hace una collection group query global sobre asignaciones activas de
            // ADMINISTRADOR en el mismo proyecto de emulador compartido por toda la suite, y no
            // debe ver "administradores" fantasma creados por este test.
            await usuarioRepository.ProvisionarAsync(new UsuarioProvisioningRequest(
                $"persona-{Guid.NewGuid():N}", $"usuario-{Guid.NewGuid():N}",
                Provider, uid, "organizador-sync@test.com", "ORGANIZADOR", "actor-1"));

            var sut = new AuthService(usuarioRepository, identityProvider.Object);
            var resultado = await sut.SyncClienteAsync(uid, "organizador-sync@test.com", new SyncClienteRequest(null, null, null));

            Assert.DoesNotContain("CLIENTE", resultado.Roles);
            Assert.False(resultado.ClaimsUpdated);
            identityProvider.Verify(p => p.SetTemporaryClaimAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()), Times.Never);
        }
    }
}
