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

        // API-MVP 3 (docs/api-mvp-plan.md §4): POST /api/events/{eventId}/controls/{controlPersonaId}
        // recibe una PersonaId, no una UsuarioId; esta es la resolución inversa que lo habilita.
        [FirestoreEmulatorFact]
        public async Task GetByPersonaIdAsync_ReturnsUsuario_WhenPersonaHasOne()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-{Guid.NewGuid():N}";

            await sut.ProvisionarAsync(NewRequest(personaId, usuarioId, $"uid-{Guid.NewGuid():N}", "control@test.com", "CONTROL"));

            var usuario = await sut.GetByPersonaIdAsync(personaId);

            Assert.NotNull(usuario);
            Assert.Equal(usuarioId, usuario!.Id);
            Assert.Equal(personaId, usuario.PersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task GetByPersonaIdAsync_ReturnsNull_WhenNoUsuarioHasThatPersonaId()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);

            var usuario = await sut.GetByPersonaIdAsync($"persona-inexistente-{Guid.NewGuid():N}");

            Assert.Null(usuario);
        }

        // API-MVP 5 (docs/api-mvp-plan.md §6): resolución batch usada al listar los Controles del
        // ámbito de un Organizador o asignados a un evento, en vez de una lectura por Control.
        [FirestoreEmulatorFact]
        public async Task GetByPersonaIdsAsync_ReturnsOnlyMatchingUsuarios_DeduplicatedAndIgnoringUnknownIds()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var personaIdA = $"persona-{Guid.NewGuid():N}";
            var personaIdB = $"persona-{Guid.NewGuid():N}";
            await sut.ProvisionarAsync(NewRequest(personaIdA, $"usuario-{Guid.NewGuid():N}", $"uid-{Guid.NewGuid():N}", "control-a@test.com", "CONTROL"));
            await sut.ProvisionarAsync(NewRequest(personaIdB, $"usuario-{Guid.NewGuid():N}", $"uid-{Guid.NewGuid():N}", "control-b@test.com", "CONTROL"));

            var resultado = await sut.GetByPersonaIdsAsync(new[]
            {
                personaIdA, personaIdA, personaIdB, $"persona-inexistente-{Guid.NewGuid():N}"
            });

            Assert.Equal(2, resultado.Count);
            Assert.Contains(resultado, u => u.PersonaId == personaIdA);
            Assert.Contains(resultado, u => u.PersonaId == personaIdB);
        }

        [FirestoreEmulatorFact]
        public async Task GetByPersonaIdsAsync_ReturnsEmpty_WhenNoIdsGiven()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);

            var resultado = await sut.GetByPersonaIdsAsync(Array.Empty<string>());

            Assert.Empty(resultado);
        }

        // La colección raíz "roles" (catálogo de Rol, Etapa 1) comparte Id de colección
        // ("roles") con la subcolección usuarios/{UsuarioId}/roles que usa este método: una
        // collection group query ingenua sobre "roles" devuelve también los documentos Rol del
        // catálogo (Codigo == rolCodigo, Activo == true calzan igual). Este test fija ese caso:
        // un catálogo con el Rol pedido activo, pero ningún Usuario con esa asignación, debe
        // devolver una lista vacía, nunca lanzar ni confundir el Rol del catálogo con un Usuario.
        [FirestoreEmulatorFact]
        public async Task GetUsuarioIdsConRolActivoAsync_IgnoresRootRolesCatalog_AndOnlyMatchesUsuarioAssignments()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var rolCodigo = $"ROL_TEST_{Guid.NewGuid():N}".ToUpperInvariant();

            await _fixture.Db!.Collection("roles").Document(rolCodigo)
                .SetAsync(new Rol { Codigo = rolCodigo, Nombre = "Rol de prueba", Activo = true });

            var sinAsignaciones = await sut.GetUsuarioIdsConRolActivoAsync(rolCodigo);
            Assert.Empty(sinAsignaciones);

            var usuarioId = $"usuario-{Guid.NewGuid():N}";
            await sut.ProvisionarAsync(NewRequest(
                $"persona-{Guid.NewGuid():N}", usuarioId, $"uid-{Guid.NewGuid():N}", "rol-test@test.com", rolCodigo));

            var conAsignacion = await sut.GetUsuarioIdsConRolActivoAsync(rolCodigo);
            Assert.Equal(new[] { usuarioId }, conAsignacion);
        }
    }
}
