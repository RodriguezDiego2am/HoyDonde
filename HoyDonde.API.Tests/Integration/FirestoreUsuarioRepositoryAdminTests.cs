using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Administración de UsuarioRol/Usuario contra Firestore Emulator real
    // (docs/security-refactor-plan.md §6, Etapa 5), en escenarios que NUNCA involucran al Rol
    // ADMINISTRADOR: el guard del último Administrador no debe intervenir acá (se cubre aparte
    // en UltimoAdministradorGuardEmulatorTests, que sí necesita controlar el conteo global).
    // Un no-op idempotente (mismo estado ya vigente) no debe tocar metadata ni auditar.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreUsuarioRepositoryAdminTests
    {
        private const string Provider = "FIREBASE";
        private readonly FirestoreEmulatorFixture _fixture;

        public FirestoreUsuarioRepositoryAdminTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static SecurityAudit NuevoAudit(string operacion, string targetId) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ActorUsuarioId = "actor-usuario-test",
            ActorPersonaId = "actor-persona-test",
            Operacion = operacion,
            TargetTipo = "Usuario",
            TargetId = targetId,
            Detalle = "test",
        };

        private async Task<bool> AuditoriaExisteAsync(string auditId)
        {
            var snapshot = await _fixture.Db!.Collection("security_audits").Document(auditId).GetSnapshotAsync();
            return snapshot.Exists;
        }

        // Igual que en FirestoreRolRepositoryAdminTests: cuenta por TargetId (no el total de la
        // colección compartida) para poder afirmar de forma confiable que un no-op no agregó
        // ninguna auditoría nueva.
        private async Task<int> ContarAuditoriasPorTargetIdAsync(string targetId)
        {
            var snapshot = await _fixture.Db!.Collection("security_audits")
                .WhereEqualTo(nameof(SecurityAudit.TargetId), targetId).GetSnapshotAsync();
            return snapshot.Count;
        }

        private async Task<string> CrearUsuarioClienteAsync(FirestoreUsuarioRepository sut)
        {
            var personaId = $"persona-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-{Guid.NewGuid():N}";
            var externalSubjectId = $"uid-{Guid.NewGuid():N}";
            await sut.ProvisionarAsync(new UsuarioProvisioningRequest(
                personaId, usuarioId, Provider, externalSubjectId, "cliente-admin-test@test.com", "CLIENTE", "test"));
            return usuarioId;
        }

        [FirestoreEmulatorFact]
        public async Task AsignarRolAsync_NuevaAsignacion_PersistsAndWritesAudit()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            var audit = NuevoAudit("USUARIO_ASIGNAR_ROL", $"{usuarioId}/ORGANIZADOR");

            await sut.AsignarRolAsync(usuarioId, "ORGANIZADOR", "tester", audit);

            var roles = await sut.GetRolCodigosActivosAsync(usuarioId);
            Assert.Contains("ORGANIZADOR", roles);
            Assert.Contains("CLIENTE", roles);
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarRolAsync_YaActivo_IsNoOp_DoesNotChangeAssignedBy_AndDoesNotWriteAudit()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            var targetId = $"{usuarioId}/ORGANIZADOR";
            await sut.AsignarRolAsync(usuarioId, "ORGANIZADOR", "tester-original", NuevoAudit("USUARIO_ASIGNAR_ROL", targetId));
            var antes = await ContarAuditoriasPorTargetIdAsync(targetId);

            var auditNoOp = NuevoAudit("USUARIO_ASIGNAR_ROL", targetId);
            await sut.AsignarRolAsync(usuarioId, "ORGANIZADOR", "tester-repetido", auditNoOp);

            var roles = await sut.GetRolCodigosActivosAsync(usuarioId);
            Assert.Single(roles, r => r == "ORGANIZADOR");
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(targetId));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarRolAsync_ReactivatesInactiveAssignment_AndWritesAudit()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            await sut.AsignarRolAsync(usuarioId, "ORGANIZADOR", "tester", NuevoAudit("USUARIO_ASIGNAR_ROL", usuarioId));
            await sut.QuitarRolAsync(usuarioId, "ORGANIZADOR", NuevoAudit("USUARIO_QUITAR_ROL", usuarioId));
            Assert.DoesNotContain("ORGANIZADOR", await sut.GetRolCodigosActivosAsync(usuarioId));

            var auditReactivacion = NuevoAudit("USUARIO_ASIGNAR_ROL", usuarioId);
            await sut.AsignarRolAsync(usuarioId, "ORGANIZADOR", "tester", auditReactivacion);

            Assert.Contains("ORGANIZADOR", await sut.GetRolCodigosActivosAsync(usuarioId));
            Assert.True(await AuditoriaExisteAsync(auditReactivacion.Id));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarRolAsync_UsuarioInexistente_ThrowsUsuarioNoEncontradoException()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);

            await Assert.ThrowsAsync<UsuarioNoEncontradoException>(() =>
                sut.AsignarRolAsync($"no-existe-{Guid.NewGuid():N}", "ORGANIZADOR", "tester", NuevoAudit("USUARIO_ASIGNAR_ROL", "x")));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarRolAsync_NonAdminRol_DeactivatesAndWritesAudit()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            await sut.AsignarRolAsync(usuarioId, "ORGANIZADOR", "tester", NuevoAudit("USUARIO_ASIGNAR_ROL", usuarioId));
            var audit = NuevoAudit("USUARIO_QUITAR_ROL", $"{usuarioId}/ORGANIZADOR");

            await sut.QuitarRolAsync(usuarioId, "ORGANIZADOR", audit);

            Assert.DoesNotContain("ORGANIZADOR", await sut.GetRolCodigosActivosAsync(usuarioId));
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarRolAsync_IsIdempotent_WhenNotAssigned_AndDoesNotWriteAudit()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            var targetId = $"{usuarioId}/ORGANIZADOR";
            var antes = await ContarAuditoriasPorTargetIdAsync(targetId);

            // Nunca se asignó ORGANIZADOR: quitar algo ausente no debe fallar ni auditar.
            var auditNoOp = NuevoAudit("USUARIO_QUITAR_ROL", targetId);
            await sut.QuitarRolAsync(usuarioId, "ORGANIZADOR", auditNoOp);

            Assert.DoesNotContain("ORGANIZADOR", await sut.GetRolCodigosActivosAsync(usuarioId));
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(targetId));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarRolAsync_IsIdempotent_WhenAlreadyInactive_AndDoesNotWriteAudit()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            await sut.AsignarRolAsync(usuarioId, "ORGANIZADOR", "tester", NuevoAudit("USUARIO_ASIGNAR_ROL", usuarioId));
            var targetId = $"{usuarioId}/ORGANIZADOR";
            await sut.QuitarRolAsync(usuarioId, "ORGANIZADOR", NuevoAudit("USUARIO_QUITAR_ROL", targetId));
            var antes = await ContarAuditoriasPorTargetIdAsync(targetId);

            var auditNoOp = NuevoAudit("USUARIO_QUITAR_ROL", targetId);
            await sut.QuitarRolAsync(usuarioId, "ORGANIZADOR", auditNoOp);

            Assert.DoesNotContain("ORGANIZADOR", await sut.GetRolCodigosActivosAsync(usuarioId));
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(targetId));
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_NonAdminUsuario_TogglesIsActive_AndWritesAudit_WithoutGuardInterference()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            var audit = NuevoAudit("USUARIO_DESACTIVAR", usuarioId);

            await sut.SetActivoAsync(usuarioId, false, audit);

            var usuario = await sut.GetByIdAsync(usuarioId);
            Assert.False(usuario!.IsActive);
            Assert.True(await AuditoriaExisteAsync(audit.Id));

            var auditReactivar = NuevoAudit("USUARIO_ACTIVAR", usuarioId);
            await sut.SetActivoAsync(usuarioId, true, auditReactivar);
            usuario = await sut.GetByIdAsync(usuarioId);
            Assert.True(usuario!.IsActive);
            Assert.True(await AuditoriaExisteAsync(auditReactivar.Id));
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_YaEnEseEstado_IsNoOp_AndDoesNotWriteAudit()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);
            var antes = await ContarAuditoriasPorTargetIdAsync(usuarioId);

            // El usuario ya está IsActive == true (default de aprovisionamiento); activarlo de
            // nuevo debe ser un no-op.
            var auditNoOp = NuevoAudit("USUARIO_ACTIVAR", usuarioId);
            await sut.SetActivoAsync(usuarioId, true, auditNoOp);

            var usuario = await sut.GetByIdAsync(usuarioId);
            Assert.True(usuario!.IsActive);
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(usuarioId));
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_Inexistente_ThrowsUsuarioNoEncontradoException()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);

            await Assert.ThrowsAsync<UsuarioNoEncontradoException>(() =>
                sut.SetActivoAsync($"no-existe-{Guid.NewGuid():N}", false, NuevoAudit("USUARIO_DESACTIVAR", "x")));
        }

        [FirestoreEmulatorFact]
        public async Task GetAllAsync_IncludesCreatedUsuario_WithMinimalFields()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearUsuarioClienteAsync(sut);

            var usuarios = await sut.GetAllAsync();

            var resumen = Assert.Single(usuarios, u => u.UsuarioId == usuarioId);
            Assert.NotEmpty(resumen.PersonaId);
            Assert.Equal("cliente-admin-test@test.com", resumen.Email);
            Assert.True(resumen.Activo);
            Assert.Contains("CLIENTE", resumen.RolesActivos);
        }
    }
}
