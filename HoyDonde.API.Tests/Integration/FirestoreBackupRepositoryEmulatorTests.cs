using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // firestore_backups contra Firestore Emulator real. Cubre lo que un mock no puede probar: el
    // round-trip de los tres enums y del DateTime? nullable, el orden descendente de GetAllAsync,
    // y sobre todo las dos operaciones transaccionales de las que depende la seguridad del módulo
    // (creación atómica del par restore+copia previa, y el claim exclusivo antes de importar).
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreBackupRepositoryEmulatorTests : IAsyncLifetime
    {
        private const string CollectionName = "firestore_backups";
        private readonly FirestoreEmulatorFixture _fixture;
        private readonly FirestoreBackupRepository? _sut;
        private readonly List<string> _creados = new();

        public FirestoreBackupRepositoryEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
            if (_fixture.Db != null) _sut = new FirestoreBackupRepository(_fixture.Db);
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_fixture.Db == null) return;
            foreach (var id in _creados)
            {
                await _fixture.Db.Collection(CollectionName).Document(id).DeleteAsync();
            }
        }

        private FirestoreBackup Nuevo(
            FirestoreBackup.BackupOperation operation = FirestoreBackup.BackupOperation.Export,
            FirestoreBackup.BackupState state = FirestoreBackup.BackupState.Pending,
            FirestoreBackup.BackupKind kind = FirestoreBackup.BackupKind.Manual,
            DateTime? createdAt = null)
        {
            var backup = new FirestoreBackup
            {
                Id = $"test-backup-{Guid.NewGuid():N}",
                Kind = kind,
                Operation = operation,
                State = state,
                StorageUri = "gs://test-bucket/firestore-backups/x",
                RequestedByUsuarioId = "usuario-test",
                RequestedByPersonaId = "persona-test",
                CreatedAt = createdAt ?? DateTime.UtcNow,
            };
            _creados.Add(backup.Id);
            return backup;
        }

        [FirestoreEmulatorFact]
        public async Task CreateAsync_RoundTripsEveryEnum_AndTheNullableCompletedAt()
        {
            var backup = Nuevo(FirestoreBackup.BackupOperation.Import, FirestoreBackup.BackupState.Running, FirestoreBackup.BackupKind.Restore);
            backup.SourceBackupId = "origen";
            await _sut!.CreateAsync(backup);

            var sinCompletar = await _sut.GetByIdAsync(backup.Id);
            Assert.NotNull(sinCompletar);
            Assert.Equal(FirestoreBackup.BackupKind.Restore, sinCompletar!.Kind);
            Assert.Equal(FirestoreBackup.BackupOperation.Import, sinCompletar.Operation);
            Assert.Equal(FirestoreBackup.BackupState.Running, sinCompletar.State);
            Assert.Equal("origen", sinCompletar.SourceBackupId);
            Assert.Null(sinCompletar.CompletedAt);

            sinCompletar.State = FirestoreBackup.BackupState.Succeeded;
            sinCompletar.CompletedAt = DateTime.UtcNow;
            await _sut.UpdateAsync(sinCompletar);

            var completado = await _sut.GetByIdAsync(backup.Id);
            Assert.Equal(FirestoreBackup.BackupState.Succeeded, completado!.State);
            Assert.NotNull(completado.CompletedAt);
        }

        [FirestoreEmulatorFact]
        public async Task GetAllAsync_ReturnsNewestFirst()
        {
            var viejo = Nuevo(createdAt: DateTime.UtcNow.AddMinutes(-30));
            var nuevo = Nuevo(createdAt: DateTime.UtcNow);
            await _sut!.CreateAsync(viejo);
            await _sut.CreateAsync(nuevo);

            var todos = await _sut.GetAllAsync();
            var propios = todos.Where(b => b.Id == viejo.Id || b.Id == nuevo.Id).ToList();

            Assert.Equal(new[] { nuevo.Id, viejo.Id }, propios.Select(b => b.Id).ToArray());
        }

        [FirestoreEmulatorFact]
        public async Task CreateAsync_OnAnExistingId_Fails_AndNeverOverwrites()
        {
            var backup = Nuevo();
            await _sut!.CreateAsync(backup);

            var colision = new FirestoreBackup { Id = backup.Id, Kind = FirestoreBackup.BackupKind.Restore };
            await Assert.ThrowsAnyAsync<Exception>(() => _sut.CreateAsync(colision));

            var persistido = await _sut.GetByIdAsync(backup.Id);
            Assert.Equal(FirestoreBackup.BackupKind.Manual, persistido!.Kind);
        }

        private (FirestoreBackup Restore, FirestoreBackup Safety) NuevoPar()
        {
            var restore = Nuevo(FirestoreBackup.BackupOperation.Import, kind: FirestoreBackup.BackupKind.Restore);
            var safety = Nuevo(kind: FirestoreBackup.BackupKind.AutomaticPreRestore);
            safety.ContinueRestoreId = restore.Id;
            return (restore, safety);
        }

        // La guarda es global y persistente, así que cada test que la ejerce arranca liberándola.
        private async Task LiberarGuardaAsync()
        {
            var (restore, safety) = NuevoPar();
            await _sut!.TryCreateRestorePairAsync(restore, safety);
            await _sut.TryTransitionAsync(restore.Id, FirestoreBackup.BackupState.Pending, record => record.State = FirestoreBackup.BackupState.Failed);
        }

        [FirestoreEmulatorFact]
        public async Task TryCreateRestorePairAsync_WritesBoth_OrNeither()
        {
            await LiberarGuardaAsync();
            var (restore, safety) = NuevoPar();

            Assert.True(await _sut!.TryCreateRestorePairAsync(restore, safety));
            Assert.NotNull(await _sut.GetByIdAsync(restore.Id));
            Assert.NotNull(await _sut.GetByIdAsync(safety.Id));

            await _sut.TryTransitionAsync(restore.Id, FirestoreBackup.BackupState.Pending, record => record.State = FirestoreBackup.BackupState.Failed);

            // Si el segundo documento colisiona, el primero tampoco debe quedar escrito: es
            // exactamente el caso que dejaba una restauración Pending huérfana para siempre.
            var (otroRestore, _) = NuevoPar();
            var safetyRepetido = new FirestoreBackup { Id = safety.Id, Kind = FirestoreBackup.BackupKind.AutomaticPreRestore };

            await Assert.ThrowsAnyAsync<Exception>(() => _sut.TryCreateRestorePairAsync(otroRestore, safetyRepetido));
            Assert.Null(await _sut.GetByIdAsync(otroRestore.Id));
        }

        [FirestoreEmulatorFact]
        public async Task TryCreateRestorePairAsync_RefusesWhileAnotherRestoreIsUnfinished_AndAllowsOneAfterItSettles()
        {
            await LiberarGuardaAsync();
            var (primero, safetyPrimero) = NuevoPar();
            Assert.True(await _sut!.TryCreateRestorePairAsync(primero, safetyPrimero));

            // Otra copia de origen, otra restauración: la guarda es global, no por documento.
            var (segundo, safetySegundo) = NuevoPar();
            Assert.False(await _sut.TryCreateRestorePairAsync(segundo, safetySegundo));
            Assert.Null(await _sut.GetByIdAsync(segundo.Id));
            Assert.Null(await _sut.GetByIdAsync(safetySegundo.Id));

            await _sut.TryTransitionAsync(primero.Id, FirestoreBackup.BackupState.Pending, record => record.State = FirestoreBackup.BackupState.Failed);

            Assert.True(await _sut.TryCreateRestorePairAsync(segundo, safetySegundo));
        }

        [FirestoreEmulatorFact]
        public async Task TryCreateRestorePairAsync_UnderConcurrency_AdmitsExactlyOneRestore()
        {
            // La invariante real que protege producción: aunque varios administradores pidan
            // restaurar a la vez copias distintas, solo una importación puede quedar en curso.
            await LiberarGuardaAsync();
            var pares = Enumerable.Range(0, 8).Select(_ => NuevoPar()).ToList();

            var resultados = await Task.WhenAll(pares.Select(par => _sut!.TryCreateRestorePairAsync(par.Restore, par.Safety)));

            Assert.Equal(1, resultados.Count(admitido => admitido));
            var admitidos = new List<string>();
            foreach (var par in pares)
            {
                if (await _sut!.GetByIdAsync(par.Restore.Id) != null) admitidos.Add(par.Restore.Id);
            }
            Assert.Single(admitidos);
        }

        [FirestoreEmulatorFact]
        public async Task TryTransitionAsync_OnlyAppliesFromTheExpectedState()
        {
            var backup = Nuevo(state: FirestoreBackup.BackupState.Pending);
            await _sut!.CreateAsync(backup);

            var desdeEstadoEquivocado = await _sut.TryTransitionAsync(backup.Id, FirestoreBackup.BackupState.Running, record => record.State = FirestoreBackup.BackupState.Failed);
            Assert.False(desdeEstadoEquivocado);
            Assert.Equal(FirestoreBackup.BackupState.Pending, (await _sut.GetByIdAsync(backup.Id))!.State);

            var correcta = await _sut.TryTransitionAsync(backup.Id, FirestoreBackup.BackupState.Pending, record => record.State = FirestoreBackup.BackupState.Running);
            Assert.True(correcta);
            Assert.Equal(FirestoreBackup.BackupState.Running, (await _sut.GetByIdAsync(backup.Id))!.State);

            Assert.False(await _sut.TryTransitionAsync($"inexistente-{Guid.NewGuid():N}", FirestoreBackup.BackupState.Pending, _ => { }));
        }

        [FirestoreEmulatorFact]
        public async Task TryTransitionAsync_UnderConcurrency_LetsExactlyOneCallerClaimTheRestore()
        {
            // La invariante que protege producción: una restauración dispara importDocuments una
            // sola vez, aunque varios administradores refresquen el historial en el mismo instante.
            var restore = Nuevo(FirestoreBackup.BackupOperation.Import, FirestoreBackup.BackupState.Pending, FirestoreBackup.BackupKind.Restore);
            await _sut!.CreateAsync(restore);

            var intentos = Enumerable.Range(0, 8).Select(_ =>
                _sut.TryTransitionAsync(restore.Id, FirestoreBackup.BackupState.Pending, record => record.State = FirestoreBackup.BackupState.Running));
            var resultados = await Task.WhenAll(intentos);

            Assert.Equal(1, resultados.Count(ganado => ganado));
            Assert.Equal(FirestoreBackup.BackupState.Running, (await _sut.GetByIdAsync(restore.Id))!.State);
        }

        [FirestoreEmulatorFact]
        public async Task DeleteAsync_RemovesOnlyThatRecord()
        {
            var borrado = Nuevo();
            var sobreviviente = Nuevo();
            await _sut!.CreateAsync(borrado);
            await _sut.CreateAsync(sobreviviente);

            await _sut.DeleteAsync(borrado.Id);

            Assert.Null(await _sut.GetByIdAsync(borrado.Id));
            Assert.NotNull(await _sut.GetByIdAsync(sobreviviente.Id));
        }
    }
}
