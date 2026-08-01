using System;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Guard transaccional de "no dejar cero Administradores efectivos" contra Firestore
    // Emulator real (docs/security-refactor-plan.md §6, Etapa 5, criterio E). El emulador es
    // compartido por toda la corrida de `dotnet test` (ver FirestoreEmulatorFixture): otras
    // suites (por ejemplo BootstrapAdminCommandEmulatorTests) dejan un Administrador efectivo
    // real y persistente en la misma base. InitializeAsync neutraliza cualquier asignación
    // ADMINISTRADOR activa preexistente (escritura directa, sin pasar por el guard, que
    // rechazaría desactivar al último) para que cada test de esta clase arranque desde un
    // conteo efectivo de cero y controle exactamente los administradores que crea.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class UltimoAdministradorGuardEmulatorTests : IAsyncLifetime
    {
        private const string Provider = "FIREBASE";
        private const string RolAdministrador = "ADMINISTRADOR";

        private readonly FirestoreEmulatorFixture _fixture;

        public UltimoAdministradorGuardEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            if (_fixture.Db == null) return;

            var rolRef = _fixture.Db.Collection("roles").Document(RolAdministrador);
            await rolRef.SetAsync(new Rol { Codigo = RolAdministrador, Nombre = "Administrador", Activo = true }, SetOptions.MergeAll);

            var query = _fixture.Db.CollectionGroup("roles").WhereEqualTo(nameof(UsuarioRol.Activo), true);
            var snapshot = await query.GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                if (doc.Id != RolAdministrador) continue;
                var usuarioRef = doc.Reference.Parent.Parent;
                if (usuarioRef == null || usuarioRef.Parent.Id != "usuarios") continue; // descarta el catálogo raíz

                await doc.Reference.UpdateAsync(nameof(UsuarioRol.Activo), false);
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private static SecurityAudit NuevoAudit(string operacion, string targetId) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ActorUsuarioId = "actor-usuario-test",
            ActorPersonaId = "actor-persona-test",
            Operacion = operacion,
            TargetTipo = "Guard",
            TargetId = targetId,
            Detalle = "test",
        };

        private async Task<bool> AuditoriaExisteAsync(string auditId)
        {
            var snapshot = await _fixture.Db!.Collection("security_audits").Document(auditId).GetSnapshotAsync();
            return snapshot.Exists;
        }

        private async Task<string> CrearAdministradorAsync(FirestoreUsuarioRepository sut)
        {
            var personaId = $"persona-admin-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-admin-{Guid.NewGuid():N}";
            var externalSubjectId = $"uid-admin-{Guid.NewGuid():N}";
            await sut.ProvisionarAsync(new UsuarioProvisioningRequest(
                personaId, usuarioId, Provider, externalSubjectId, $"{usuarioId}@test.com", RolAdministrador, "test"));
            return usuarioId;
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_UnicoAdministradorEfectivo_IsRejected_AndDoesNotModifyState()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearAdministradorAsync(sut);
            var auditRechazado = NuevoAudit("USUARIO_DESACTIVAR", usuarioId);

            await Assert.ThrowsAsync<UltimoAdministradorException>(() => sut.SetActivoAsync(usuarioId, false, auditRechazado));

            var usuario = await sut.GetByIdAsync(usuarioId);
            Assert.True(usuario!.IsActive);
            Assert.Contains(RolAdministrador, await sut.GetRolCodigosActivosAsync(usuarioId));
            Assert.False(await AuditoriaExisteAsync(auditRechazado.Id));
        }

        [FirestoreEmulatorFact]
        public async Task QuitarRolAsync_UltimoRolAdministrador_IsRejected_AndDoesNotModifyState()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = await CrearAdministradorAsync(sut);
            var auditRechazado = NuevoAudit("USUARIO_QUITAR_ROL", usuarioId);

            await Assert.ThrowsAsync<UltimoAdministradorException>(() => sut.QuitarRolAsync(usuarioId, RolAdministrador, auditRechazado));

            Assert.Contains(RolAdministrador, await sut.GetRolCodigosActivosAsync(usuarioId));
            Assert.False(await AuditoriaExisteAsync(auditRechazado.Id));
        }

        [FirestoreEmulatorFact]
        public async Task RolSetActivoAsync_DesactivarAdministrador_DejandoCeroEfectivos_IsRejected_AndDoesNotModifyState()
        {
            var usuarioRepo = new FirestoreUsuarioRepository(_fixture.Db!);
            var rolRepo = new FirestoreRolRepository(_fixture.Db!);
            var usuarioId = await CrearAdministradorAsync(usuarioRepo);
            var auditRechazado = NuevoAudit("ROL_ACTIVAR", RolAdministrador);

            await Assert.ThrowsAsync<UltimoAdministradorException>(() => rolRepo.SetActivoAsync(RolAdministrador, false, auditRechazado));

            var rol = await rolRepo.GetByCodigoAsync(RolAdministrador);
            Assert.True(rol!.Activo);
            Assert.False(await AuditoriaExisteAsync(auditRechazado.Id));
            // El usuario tampoco debería haberse tocado.
            var usuario = await usuarioRepo.GetByIdAsync(usuarioId);
            Assert.True(usuario!.IsActive);
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_ConDosAdministradores_PuedeDesactivarUno()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var admin1 = await CrearAdministradorAsync(sut);
            var admin2 = await CrearAdministradorAsync(sut);

            await sut.SetActivoAsync(admin1, false, NuevoAudit("USUARIO_DESACTIVAR", admin1));

            var usuario1 = await sut.GetByIdAsync(admin1);
            Assert.False(usuario1!.IsActive);
            var usuario2 = await sut.GetByIdAsync(admin2);
            Assert.True(usuario2!.IsActive);

            // El segundo, ahora único efectivo, ya no se puede desactivar.
            await Assert.ThrowsAsync<UltimoAdministradorException>(() =>
                sut.SetActivoAsync(admin2, false, NuevoAudit("USUARIO_DESACTIVAR", admin2)));
        }

        [FirestoreEmulatorFact]
        public async Task SetActivoAsync_DosDesactivacionesConcurrentesSobreLosDosUltimosAdministradores_DejaExactamenteUnoEfectivo()
        {
            var sut = new FirestoreUsuarioRepository(_fixture.Db!);
            var admin1 = await CrearAdministradorAsync(sut);
            var admin2 = await CrearAdministradorAsync(sut);

            var tarea1 = SafeSetActivoAsync(sut, admin1, NuevoAudit("USUARIO_DESACTIVAR", admin1));
            var tarea2 = SafeSetActivoAsync(sut, admin2, NuevoAudit("USUARIO_DESACTIVAR", admin2));

            var resultados = await Task.WhenAll(tarea1, tarea2);

            var exitosas = resultados.Count(r => r);
            var rechazadas = resultados.Count(r => !r);
            Assert.Equal(1, exitosas);
            Assert.Equal(1, rechazadas);

            var usuario1 = await sut.GetByIdAsync(admin1);
            var usuario2 = await sut.GetByIdAsync(admin2);
            var efectivos = new[] { usuario1!.IsActive, usuario2!.IsActive }.Count(activo => activo);
            Assert.Equal(1, efectivos);
        }

        // true si SetActivoAsync tuvo éxito, false si fue rechazada por UltimoAdministradorException
        // (cualquier otra excepción se deja propagar: no forma parte del contrato esperado).
        private static async Task<bool> SafeSetActivoAsync(FirestoreUsuarioRepository sut, string usuarioId, SecurityAudit auditEntry)
        {
            try
            {
                await sut.SetActivoAsync(usuarioId, false, auditEntry);
                return true;
            }
            catch (UltimoAdministradorException)
            {
                return false;
            }
        }
    }
}
