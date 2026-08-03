using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Administración de Rol/RolAccion contra Firestore Emulator real
    // (docs/security-refactor-plan.md §6, Etapa 5): cada mutación real persiste junto con su
    // SecurityAudit en la misma transacción; una operación rechazada, o un no-op idempotente
    // (mismo estado ya vigente), no deja cambios parciales ni auditoría.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreRolRepositoryAdminTests : IAsyncLifetime
    {
        private const string Provider = "FIREBASE";
        private readonly FirestoreEmulatorFixture _fixture;
        private readonly string _codigo = $"TEST_ADMIN_ROL_{Guid.NewGuid():N}".ToUpperInvariant();
        private FirestoreRolRepository? _sut;
        private FirestoreUsuarioRepository? _usuarioSut;

        public FirestoreRolRepositoryAdminTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
            if (_fixture.Db != null)
            {
                _sut = new FirestoreRolRepository(_fixture.Db);
                _usuarioSut = new FirestoreUsuarioRepository(_fixture.Db);
            }
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_fixture.Db == null) return;
            var docRef = _fixture.Db.Collection("roles").Document(_codigo);
            var acciones = await docRef.Collection("acciones").GetSnapshotAsync();
            foreach (var doc in acciones.Documents) await doc.Reference.DeleteAsync();
            await docRef.DeleteAsync();
        }

        private static SecurityAudit NuevoAudit(string operacion, string targetId) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ActorUsuarioId = "actor-usuario-test",
            ActorPersonaId = "actor-persona-test",
            Operacion = operacion,
            TargetTipo = "Rol",
            TargetId = targetId,
            Detalle = "test",
        };

        private async Task<bool> AuditoriaExisteAsync(string auditId)
        {
            var snapshot = await _fixture.Db!.Collection("security_audits").Document(auditId).GetSnapshotAsync();
            return snapshot.Exists;
        }

        // Cuenta las auditorías vinculadas a un TargetId dado -no el total de la colección
        // compartida por toda la corrida-, para poder afirmar "el no-op no agregó ninguna
        // auditoría nueva" con un antes/después confiable incluso en paralelo con otras suites.
        private async Task<int> ContarAuditoriasPorTargetIdAsync(string targetId)
        {
            var snapshot = await _fixture.Db!.Collection("security_audits")
                .WhereEqualTo(nameof(SecurityAudit.TargetId), targetId).GetSnapshotAsync();
            return snapshot.Count;
        }

        [FirestoreEmulatorFact]
        public async Task CrearAsync_PersistsRol_AndAudit()
        {
            var audit = NuevoAudit("ROL_CREAR", _codigo);

            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, audit);

            var rol = await _sut.GetByCodigoAsync(_codigo);
            Assert.NotNull(rol);
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task CrearAsync_DuplicateCodigo_ThrowsRolYaExisteException_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));

            var auditRechazado = NuevoAudit("ROL_CREAR", _codigo);
            await Assert.ThrowsAsync<RolYaExisteException>(() =>
                _sut.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Otro", Descripcion = "y" }, auditRechazado));

            Assert.False(await AuditoriaExisteAsync(auditRechazado.Id));
        }

        [FirestoreEmulatorFact]
        public async Task EditarAsync_UpdatesNombreYDescripcion_AndWritesAudit_WithoutChangingCodigo()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Original", Descripcion = "original" }, NuevoAudit("ROL_CREAR", _codigo));
            var audit = NuevoAudit("ROL_EDITAR", _codigo);

            await _sut.EditarAsync(_codigo, "Editado", "descripcion editada", audit);

            var rol = await _sut.GetByCodigoAsync(_codigo);
            Assert.Equal(_codigo, rol!.Codigo);
            Assert.Equal("Editado", rol.Nombre);
            Assert.Equal("descripcion editada", rol.Descripcion);
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task EditarAsync_Inexistente_ThrowsRolNoEncontradoException_AndDoesNotWriteAudit()
        {
            var audit = NuevoAudit("ROL_EDITAR", _codigo);

            await Assert.ThrowsAsync<RolNoEncontradoException>(() => _sut!.EditarAsync(_codigo, "x", "y", audit));

            Assert.False(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task EditarAsync_MismosValores_IsNoOp_DoesNotChangeUpdatedAt_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Original", Descripcion = "original" }, NuevoAudit("ROL_CREAR", _codigo));
            var rolAntes = await _sut.GetByCodigoAsync(_codigo);
            var antes = await ContarAuditoriasPorTargetIdAsync(_codigo);

            var auditNoOp = NuevoAudit("ROL_EDITAR", _codigo);
            await _sut.EditarAsync(_codigo, "Original", "original", auditNoOp);

            var rolDespues = await _sut.GetByCodigoAsync(_codigo);
            Assert.Equal(rolAntes!.UpdatedAt, rolDespues!.UpdatedAt);
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(_codigo));
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_NonAdminRol_TogglesActivo_AndWritesAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            var audit = NuevoAudit("ROL_ACTIVAR", _codigo);

            await _sut.SetActivoAsync(_codigo, false, audit);

            var rol = await _sut.GetByCodigoAsync(_codigo);
            Assert.False(rol!.Activo);
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_Inexistente_ThrowsRolNoEncontradoException()
        {
            await Assert.ThrowsAsync<RolNoEncontradoException>(() => _sut!.SetActivoAsync(_codigo, false, NuevoAudit("ROL_ACTIVAR", _codigo)));
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_YaEnEseEstado_IsNoOp_DoesNotChangeUpdatedAt_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            var rolAntes = await _sut.GetByCodigoAsync(_codigo);
            var antes = await ContarAuditoriasPorTargetIdAsync(_codigo);

            // El rol ya está Activo == true (default de creación); pedir activarlo de nuevo debe
            // ser un no-op.
            var auditNoOp = NuevoAudit("ROL_ACTIVAR", _codigo);
            await _sut.SetActivoAsync(_codigo, true, auditNoOp);

            var rolDespues = await _sut.GetByCodigoAsync(_codigo);
            Assert.True(rolDespues!.Activo);
            Assert.Equal(rolAntes!.UpdatedAt, rolDespues.UpdatedAt);
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(_codigo));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarAccionAsync_NuevaAsignacion_PersistsAndWritesAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            var accionCodigo = "TICKET_VALIDAR"; // parte del catálogo real sembrado por otras suites/el seeder
            var audit = NuevoAudit("ROL_ASIGNAR_ACCION", $"{_codigo}/{accionCodigo}");

            await _sut.AsignarAccionAsync(_codigo, accionCodigo, "tester", audit);

            var acciones = await _sut.GetAccionCodigosAsync(_codigo);
            Assert.Single(acciones);
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarAccionAsync_YaAsignada_IsNoOp_DoesNotChangeAssignedAt_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            var accionCodigo = "TICKET_VALIDAR";
            var targetId = $"{_codigo}/{accionCodigo}";
            await _sut.AsignarAccionAsync(_codigo, accionCodigo, "tester-original", NuevoAudit("ROL_ASIGNAR_ACCION", targetId));

            var asignacionAntes = await AsignacionAsync(accionCodigo);
            var antes = await ContarAuditoriasPorTargetIdAsync(targetId);

            var auditNoOp = NuevoAudit("ROL_ASIGNAR_ACCION", targetId);
            await _sut.AsignarAccionAsync(_codigo, accionCodigo, "tester-repetido", auditNoOp);

            var asignacionDespues = await AsignacionAsync(accionCodigo);
            var acciones = await _sut.GetAccionCodigosAsync(_codigo);
            Assert.Single(acciones);
            Assert.Equal(asignacionAntes!.AssignedBy, asignacionDespues!.AssignedBy);
            Assert.Equal(asignacionAntes.AssignedAt, asignacionDespues.AssignedAt);
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(targetId));
        }

        private async Task<RolAccionAsignacion?> AsignacionAsync(string accionCodigo)
        {
            var snapshot = await _fixture.Db!.Collection("roles").Document(_codigo)
                .Collection("acciones").Document(accionCodigo).GetSnapshotAsync();
            return snapshot.Exists ? snapshot.ConvertTo<RolAccionAsignacion>() : null;
        }

        [FirestoreEmulatorFact]
        public async Task AsignarAccionAsync_RolInexistente_ThrowsRolNoEncontradoException()
        {
            await Assert.ThrowsAsync<RolNoEncontradoException>(() =>
                _sut!.AsignarAccionAsync(_codigo, "TICKET_VALIDAR", "tester", NuevoAudit("ROL_ASIGNAR_ACCION", _codigo)));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarAccionAsync_AccionInexistente_ThrowsAccionNoEncontradaException_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            var accionInexistente = $"ACCION_NO_EXISTE_{Guid.NewGuid():N}".ToUpperInvariant();
            var audit = NuevoAudit("ROL_ASIGNAR_ACCION", $"{_codigo}/{accionInexistente}");

            await Assert.ThrowsAsync<AccionNoEncontradaException>(() => _sut.AsignarAccionAsync(_codigo, accionInexistente, "tester", audit));

            Assert.False(await AuditoriaExisteAsync(audit.Id));
            Assert.DoesNotContain(accionInexistente, await _sut.GetAccionCodigosAsync(_codigo));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarAccionAsync_Asignada_RemovesAndWritesAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            await _sut.AsignarAccionAsync(_codigo, "TICKET_VALIDAR", "tester", NuevoAudit("ROL_ASIGNAR_ACCION", _codigo));
            var audit = NuevoAudit("ROL_QUITAR_ACCION", $"{_codigo}/TICKET_VALIDAR");

            await _sut.QuitarAccionAsync(_codigo, "TICKET_VALIDAR", audit);

            Assert.Empty(await _sut.GetAccionCodigosAsync(_codigo));
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarAccionAsync_NoAsignada_IsNoOp_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            var targetId = $"{_codigo}/TICKET_VALIDAR";
            var antes = await ContarAuditoriasPorTargetIdAsync(targetId);

            // TICKET_VALIDAR nunca se asignó a este Rol: la Accion existe en el catálogo, pero
            // no está asignada -distinto de "Accion inexistente"-.
            var auditNoOp = NuevoAudit("ROL_QUITAR_ACCION", targetId);
            await _sut.QuitarAccionAsync(_codigo, "TICKET_VALIDAR", auditNoOp);

            Assert.Empty(await _sut.GetAccionCodigosAsync(_codigo));
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(targetId));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarAccionAsync_YaQuitada_IsNoOp_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            await _sut.AsignarAccionAsync(_codigo, "TICKET_VALIDAR", "tester", NuevoAudit("ROL_ASIGNAR_ACCION", _codigo));
            var targetId = $"{_codigo}/TICKET_VALIDAR";
            await _sut.QuitarAccionAsync(_codigo, "TICKET_VALIDAR", NuevoAudit("ROL_QUITAR_ACCION", targetId));
            var antes = await ContarAuditoriasPorTargetIdAsync(targetId);

            var auditNoOp = NuevoAudit("ROL_QUITAR_ACCION", targetId);
            await _sut.QuitarAccionAsync(_codigo, "TICKET_VALIDAR", auditNoOp);

            Assert.Empty(await _sut.GetAccionCodigosAsync(_codigo));
            Assert.False(await AuditoriaExisteAsync(auditNoOp.Id));
            Assert.Equal(antes, await ContarAuditoriasPorTargetIdAsync(targetId));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarAccionAsync_AccionInexistenteEnCatalogo_ThrowsAccionNoEncontradaException_AndDoesNotWriteAudit()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));
            var accionInexistente = $"ACCION_NO_EXISTE_{Guid.NewGuid():N}".ToUpperInvariant();
            var audit = NuevoAudit("ROL_QUITAR_ACCION", $"{_codigo}/{accionInexistente}");

            await Assert.ThrowsAsync<AccionNoEncontradaException>(() => _sut.QuitarAccionAsync(_codigo, accionInexistente, audit));

            Assert.False(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarAccionAsync_RolInexistente_ThrowsRolNoEncontradoException()
        {
            await Assert.ThrowsAsync<RolNoEncontradoException>(() =>
                _sut!.QuitarAccionAsync(_codigo, "TICKET_VALIDAR", NuevoAudit("ROL_QUITAR_ACCION", _codigo)));
        }

        [FirestoreEmulatorFact]
        public async Task GetAllAsync_IncludesCreatedRol()
        {
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", _codigo));

            var roles = await _sut.GetAllAsync();

            Assert.Contains(roles, r => r.Codigo == _codigo);
        }

        // ---- Baja física de un Rol (docs/api-mvp-plan.md §12) ----

        private async Task<string> CrearUsuarioClienteAsync()
        {
            var personaId = $"persona-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-{Guid.NewGuid():N}";
            var externalSubjectId = $"uid-{Guid.NewGuid():N}";
            await _usuarioSut!.ProvisionarAsync(new UsuarioProvisioningRequest(
                personaId, usuarioId, Provider, externalSubjectId, "rol-eliminar-test@test.com", "CLIENTE", "test"));
            return usuarioId;
        }

        private Task<Rol?> CrearRolPersonalizadoInactivoAsync(string codigo) =>
            CrearRolPersonalizadoAsync(codigo, activo: false);

        private async Task<Rol?> CrearRolPersonalizadoAsync(string codigo, bool activo)
        {
            await _sut!.CrearAsync(new Rol { Codigo = codigo, Nombre = "Test", Descripcion = "x" }, NuevoAudit("ROL_CREAR", codigo));
            if (!activo)
            {
                await _sut.SetActivoAsync(codigo, false, NuevoAudit("ROL_ACTIVAR", codigo));
            }
            return await _sut.GetByCodigoAsync(codigo);
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_RolPersonalizadoInactivoSinUsuarios_DeletesRolAndWritesAudit()
        {
            await CrearRolPersonalizadoInactivoAsync(_codigo);
            var audit = NuevoAudit("ROL_ELIMINAR", _codigo);

            await _sut!.EliminarAsync(_codigo, audit);

            Assert.Null(await _sut.GetByCodigoAsync(_codigo));
            Assert.True(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_DeletesAllRolAccionAssignments()
        {
            await CrearRolPersonalizadoAsync(_codigo, activo: true);
            await _sut!.AsignarAccionAsync(_codigo, "TICKET_VALIDAR", "tester", NuevoAudit("ROL_ASIGNAR_ACCION", _codigo));
            await _sut.SetActivoAsync(_codigo, false, NuevoAudit("ROL_ACTIVAR", _codigo));

            await _sut.EliminarAsync(_codigo, NuevoAudit("ROL_ELIMINAR", _codigo));

            var asignacionRestante = await _fixture.Db!.Collection("roles").Document(_codigo)
                .Collection("acciones").Document("TICKET_VALIDAR").GetSnapshotAsync();
            Assert.False(asignacionRestante.Exists);
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_NoEliminaLaAccionDelCatalogo()
        {
            await CrearRolPersonalizadoAsync(_codigo, activo: true);
            await _sut!.AsignarAccionAsync(_codigo, "TICKET_VALIDAR", "tester", NuevoAudit("ROL_ASIGNAR_ACCION", _codigo));
            await _sut.SetActivoAsync(_codigo, false, NuevoAudit("ROL_ACTIVAR", _codigo));

            await _sut.EliminarAsync(_codigo, NuevoAudit("ROL_ELIMINAR", _codigo));

            var accionSnapshot = await _fixture.Db!.Collection("acciones").Document("TICKET_VALIDAR").GetSnapshotAsync();
            Assert.True(accionSnapshot.Exists);
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_PreservesEarlierAudits_AndWritesExactlyOneNewAudit()
        {
            var auditCrear = NuevoAudit("ROL_CREAR", _codigo);
            await _sut!.CrearAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "x" }, auditCrear);
            var auditDesactivar = NuevoAudit("ROL_ACTIVAR", _codigo);
            await _sut.SetActivoAsync(_codigo, false, auditDesactivar);
            var antes = await ContarAuditoriasPorTargetIdAsync(_codigo);

            var auditEliminar = NuevoAudit("ROL_ELIMINAR", _codigo);
            await _sut.EliminarAsync(_codigo, auditEliminar);

            Assert.True(await AuditoriaExisteAsync(auditCrear.Id));
            Assert.True(await AuditoriaExisteAsync(auditDesactivar.Id));
            Assert.True(await AuditoriaExisteAsync(auditEliminar.Id));
            Assert.Equal(antes + 1, await ContarAuditoriasPorTargetIdAsync(_codigo));
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_RolActivo_ThrowsRolDebeEstarInactivoException_AndDoesNotDelete()
        {
            await CrearRolPersonalizadoAsync(_codigo, activo: true);
            var audit = NuevoAudit("ROL_ELIMINAR", _codigo);

            await Assert.ThrowsAsync<RolDebeEstarInactivoException>(() => _sut!.EliminarAsync(_codigo, audit));

            Assert.NotNull(await _sut!.GetByCodigoAsync(_codigo));
            Assert.False(await AuditoriaExisteAsync(audit.Id));
        }

        private async Task EliminarAsync_RolEsencial_ThrowsRolProtegidoExceptionAsync(string codigoEsencial)
        {
            var audit = NuevoAudit("ROL_ELIMINAR", codigoEsencial);

            await Assert.ThrowsAsync<RolProtegidoException>(() => _sut!.EliminarAsync(codigoEsencial, audit));

            Assert.False(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public Task EliminarAsync_RolEsencial_Administrador_ThrowsRolProtegidoException() =>
            EliminarAsync_RolEsencial_ThrowsRolProtegidoExceptionAsync("ADMINISTRADOR");

        [FirestoreEmulatorFact]
        public Task EliminarAsync_RolEsencial_Organizador_ThrowsRolProtegidoException() =>
            EliminarAsync_RolEsencial_ThrowsRolProtegidoExceptionAsync("ORGANIZADOR");

        [FirestoreEmulatorFact]
        public Task EliminarAsync_RolEsencial_Cliente_ThrowsRolProtegidoException() =>
            EliminarAsync_RolEsencial_ThrowsRolProtegidoExceptionAsync("CLIENTE");

        [FirestoreEmulatorFact]
        public Task EliminarAsync_RolEsencial_Control_ThrowsRolProtegidoException() =>
            EliminarAsync_RolEsencial_ThrowsRolProtegidoExceptionAsync("CONTROL");

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_Inexistente_ThrowsRolNoEncontradoException()
        {
            var audit = NuevoAudit("ROL_ELIMINAR", _codigo);

            await Assert.ThrowsAsync<RolNoEncontradoException>(() => _sut!.EliminarAsync(_codigo, audit));

            Assert.False(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_RepeticionPosterior_ThrowsRolNoEncontradoException()
        {
            await CrearRolPersonalizadoInactivoAsync(_codigo);
            await _sut!.EliminarAsync(_codigo, NuevoAudit("ROL_ELIMINAR", _codigo));

            await Assert.ThrowsAsync<RolNoEncontradoException>(() =>
                _sut.EliminarAsync(_codigo, NuevoAudit("ROL_ELIMINAR", _codigo)));
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_ConUsuarioRolActiva_ThrowsRolTieneUsuariosAsignadosException_AndDoesNotDelete()
        {
            await CrearRolPersonalizadoAsync(_codigo, activo: true);
            var usuarioId = await CrearUsuarioClienteAsync();
            await _usuarioSut!.AsignarRolAsync(usuarioId, _codigo, "tester", NuevoAudit("USUARIO_ASIGNAR_ROL", $"{usuarioId}/{_codigo}"));
            await _sut!.SetActivoAsync(_codigo, false, NuevoAudit("ROL_ACTIVAR", _codigo));

            var audit = NuevoAudit("ROL_ELIMINAR", _codigo);
            await Assert.ThrowsAsync<RolTieneUsuariosAsignadosException>(() => _sut.EliminarAsync(_codigo, audit));

            Assert.NotNull(await _sut.GetByCodigoAsync(_codigo));
            Assert.False(await AuditoriaExisteAsync(audit.Id));
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_ConUsuarioRolInactiva_ThrowsRolTieneUsuariosAsignadosException()
        {
            await CrearRolPersonalizadoAsync(_codigo, activo: true);
            var usuarioId = await CrearUsuarioClienteAsync();
            await _usuarioSut!.AsignarRolAsync(usuarioId, _codigo, "tester", NuevoAudit("USUARIO_ASIGNAR_ROL", $"{usuarioId}/{_codigo}"));
            // La asignación queda INACTIVA (no eliminada): la condición #4 de docs/api-mvp-plan.md
            // §12 exige "ninguna asignación, activa NI inactiva".
            await _usuarioSut.QuitarRolAsync(usuarioId, _codigo, NuevoAudit("USUARIO_QUITAR_ROL", $"{usuarioId}/{_codigo}"));
            await _sut!.SetActivoAsync(_codigo, false, NuevoAudit("ROL_ACTIVAR", _codigo));

            var audit = NuevoAudit("ROL_ELIMINAR", _codigo);
            await Assert.ThrowsAsync<RolTieneUsuariosAsignadosException>(() => _sut.EliminarAsync(_codigo, audit));

            Assert.NotNull(await _sut.GetByCodigoAsync(_codigo));
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_NoTocaOtrosRolesNiUsuarios()
        {
            var otroCodigo = $"TEST_ADMIN_ROL_OTRO_{Guid.NewGuid():N}".ToUpperInvariant();
            await CrearRolPersonalizadoAsync(otroCodigo, activo: true);
            await CrearRolPersonalizadoInactivoAsync(_codigo);
            var usuarioId = await CrearUsuarioClienteAsync();

            await _sut!.EliminarAsync(_codigo, NuevoAudit("ROL_ELIMINAR", _codigo));

            Assert.NotNull(await _sut.GetByCodigoAsync(otroCodigo));
            var usuario = await _usuarioSut!.GetByIdAsync(usuarioId);
            Assert.NotNull(usuario);
            Assert.Contains("CLIENTE", await _usuarioSut.GetRolCodigosActivosAsync(usuarioId));

            // Limpieza manual: DisposeAsync solo conoce _codigo.
            await _sut.SetActivoAsync(otroCodigo, false, NuevoAudit("ROL_ACTIVAR", otroCodigo));
            await _sut.EliminarAsync(otroCodigo, NuevoAudit("ROL_ELIMINAR", otroCodigo));
        }

        [FirestoreEmulatorFact]
        public async Task EliminarAsync_ConcurrentWithAsignarRol_NeverLeavesOrphanedAssignment()
        {
            await CrearRolPersonalizadoAsync(_codigo, activo: true);
            await _sut!.SetActivoAsync(_codigo, false, NuevoAudit("ROL_ACTIVAR", _codigo));
            var usuarioId = await CrearUsuarioClienteAsync();

            var eliminarTask = _sut.EliminarAsync(_codigo, NuevoAudit("ROL_ELIMINAR", _codigo));
            var asignarTask = _usuarioSut!.AsignarRolAsync(
                usuarioId, _codigo, "tester", NuevoAudit("USUARIO_ASIGNAR_ROL", $"{usuarioId}/{_codigo}"));

            var eliminarFallo = false;
            var asignarFallo = false;
            try { await eliminarTask; } catch (RolTieneUsuariosAsignadosException) { eliminarFallo = true; }
            try { await asignarTask; } catch (RolNoEncontradoException) { asignarFallo = true; }

            // Invariante: nunca ambas cosas conviven. O el Rol quedó borrado y la asignación nunca
            // se creó (asignarFallo), o el Rol sigue existiendo con la asignación real
            // (eliminarFallo) -nunca una asignación huérfana apuntando a un Rol ya borrado-.
            var rolExiste = await _sut.GetByCodigoAsync(_codigo) != null;
            var tieneAsignacion = (await _usuarioSut.GetRolCodigosActivosAsync(usuarioId)).Contains(_codigo);

            if (!rolExiste)
            {
                Assert.True(asignarFallo);
                Assert.False(tieneAsignacion);
            }
            else
            {
                Assert.True(eliminarFallo);
                Assert.True(tieneAsignacion);
                // Limpieza manual para no dejar el rol como residuo activo con usuario asignado.
                await _usuarioSut.QuitarRolAsync(usuarioId, _codigo, NuevoAudit("USUARIO_QUITAR_ROL", $"{usuarioId}/{_codigo}"));
            }
        }
    }
}
