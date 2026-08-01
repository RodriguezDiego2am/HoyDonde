using System;
using System.Threading.Tasks;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreUsuarioRepositoryTests
    {
        private const string Provider = "FIREBASE";
        private readonly FirestoreEmulatorFixture _fixture;

        public FirestoreUsuarioRepositoryTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static UsuarioProvisioningRequest NewRequest(
            string personaId, string usuarioId, string externalSubjectId, string email, string rolCodigo, string? fullName = null)
        {
            return new UsuarioProvisioningRequest(
                personaId, usuarioId, Provider, externalSubjectId, email, rolCodigo, "test", FullName: fullName);
        }

        [FirestoreEmulatorFact]
        public async Task ProvisionarAsync_CreatesPersonaUsuarioRolEIdentidadExterna_EnLaMismaTransaccion()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-{Guid.NewGuid():N}";
            var externalSubjectId = $"uid-{Guid.NewGuid():N}";

            var resultado = await sut.ProvisionarAsync(
                NewRequest(personaId, usuarioId, externalSubjectId, "persona@test.com", "CLIENTE", "Persona Test"));

            Assert.Equal(personaId, resultado.PersonaId);
            Assert.Equal(usuarioId, resultado.UsuarioId);

            var resolvedUsuarioId = await sut.GetUsuarioIdByExternalSubjectAsync(Provider, externalSubjectId);
            Assert.Equal(usuarioId, resolvedUsuarioId);

            var usuario = await sut.GetByIdAsync(usuarioId);
            Assert.NotNull(usuario);
            Assert.Equal(personaId, usuario!.PersonaId);
            Assert.True(usuario.IsActive);

            var persona = await _fixture.Db!.Collection("personas").Document(personaId).GetSnapshotAsync();
            Assert.True(persona.Exists);

            var roles = await sut.GetRolCodigosActivosAsync(usuarioId);
            Assert.Contains("CLIENTE", roles);
        }

        [FirestoreEmulatorFact]
        public async Task ProvisionarAsync_IsIdempotent_OnRetryWithSameExternalSubjectId_DoesNotDuplicateNorChangeRole()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var externalSubjectId = $"uid-{Guid.NewGuid():N}";

            var primero = await sut.ProvisionarAsync(
                NewRequest($"persona-{Guid.NewGuid():N}", $"usuario-{Guid.NewGuid():N}", externalSubjectId, "retry@test.com", "CLIENTE"));

            // Un reintento real generaría IDs y un rol distintos (el llamador no sabe todavía
            // que el aprovisionamiento anterior tuvo éxito). Deben ser ignorados por completo.
            var segundoPersonaId = $"persona-{Guid.NewGuid():N}";
            var segundoUsuarioId = $"usuario-{Guid.NewGuid():N}";
            var segundo = await sut.ProvisionarAsync(
                NewRequest(segundoPersonaId, segundoUsuarioId, externalSubjectId, "retry@test.com", "ADMINISTRADOR"));

            Assert.Equal(primero.PersonaId, segundo.PersonaId);
            Assert.Equal(primero.UsuarioId, segundo.UsuarioId);

            var roles = await sut.GetRolCodigosActivosAsync(primero.UsuarioId);
            Assert.Contains("CLIENTE", roles);
            Assert.DoesNotContain("ADMINISTRADOR", roles);

            var personaFantasma = await _fixture.Db!.Collection("personas").Document(segundoPersonaId).GetSnapshotAsync();
            Assert.False(personaFantasma.Exists);
            var usuarioFantasma = await _fixture.Db.Collection("usuarios").Document(segundoUsuarioId).GetSnapshotAsync();
            Assert.False(usuarioFantasma.Exists);
        }

        // Atomicidad todo-o-nada (docs/security-refactor-plan.md §6, Etapa 2): PersonaId y
        // UsuarioId se generan antes de la transacción; acá se precarga deliberadamente una
        // colisión en personas/{PersonaId} para forzar que transaction.Create falle al
        // confirmar, y se verifica que ni Usuario, ni UsuarioRol, ni IdentidadExterna quedan
        // creados.
        [FirestoreEmulatorFact]
        public async Task ProvisionarAsync_WhenPersonaIdAlreadyExists_TransactionFails_AndNothingElseIsCreated()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-{Guid.NewGuid():N}";
            var externalSubjectId = $"uid-{Guid.NewGuid():N}";

            // Colisión determinística: ya existe un documento en personas/{PersonaId} antes de
            // llamar a ProvisionarAsync con ese mismo PersonaId.
            await _fixture.Db!.Collection("personas").Document(personaId)
                .SetAsync(new Persona { Id = personaId, Email = "ocupado@test.com" });

            var request = NewRequest(personaId, usuarioId, externalSubjectId, "atomic@test.com", "CLIENTE");

            await Assert.ThrowsAnyAsync<Exception>(() => sut.ProvisionarAsync(request));

            var usuario = await sut.GetByIdAsync(usuarioId);
            Assert.Null(usuario);

            var roles = await sut.GetRolCodigosActivosAsync(usuarioId);
            Assert.Empty(roles);

            var usuarioIdResuelto = await sut.GetUsuarioIdByExternalSubjectAsync(Provider, externalSubjectId);
            Assert.Null(usuarioIdResuelto);
        }

        [FirestoreEmulatorFact]
        public async Task GetByIdAsync_ReturnsNull_WhenUsuarioDoesNotExist()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);

            var usuario = await sut.GetByIdAsync($"no-existe-{Guid.NewGuid():N}");

            Assert.Null(usuario);
        }
    }
}
