using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public class FirestoreBackupRepository : IFirestoreBackupRepository
    {
        private const string CollectionName = "firestore_backups";

        // Un único documento de guarda: la contención sobre un solo documento es lo que Firestore
        // serializa con garantía. Vive en su propia colección para que el historial siga siendo
        // solo copias, y nunca se limpia: la guarda relee el restore que apunta y lo ignora en
        // cuanto es terminal, así que un valor viejo jamás bloquea una restauración nueva.
        private const string LockCollectionName = "firestore_backup_locks";
        private const string RestoreLockDocumentId = "restore";
        private const string ActiveRestoreIdField = "ActiveRestoreId";
        private readonly FirestoreDb _firestore;

        public FirestoreBackupRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public Task CreateAsync(FirestoreBackup backup) =>
            _firestore.Collection(CollectionName).Document(backup.Id).CreateAsync(backup);

        public Task<bool> TryCreateRestorePairAsync(FirestoreBackup restore, FirestoreBackup safetyExport) =>
            _firestore.RunTransactionAsync(async transaction =>
            {
                var lockReference = _firestore.Collection(LockCollectionName).Document(RestoreLockDocumentId);
                var lockSnapshot = await transaction.GetSnapshotAsync(lockReference);
                if (lockSnapshot.Exists &&
                    lockSnapshot.TryGetValue<string>(ActiveRestoreIdField, out var activeRestoreId) &&
                    !string.IsNullOrWhiteSpace(activeRestoreId))
                {
                    var activeSnapshot = await transaction.GetSnapshotAsync(_firestore.Collection(CollectionName).Document(activeRestoreId));
                    if (activeSnapshot.Exists &&
                        activeSnapshot.ConvertTo<FirestoreBackup>().State is FirestoreBackup.BackupState.Pending or FirestoreBackup.BackupState.Running)
                    {
                        return false;
                    }
                }

                transaction.Set(lockReference, new Dictionary<string, object>
                {
                    [ActiveRestoreIdField] = restore.Id,
                    ["UpdatedAt"] = DateTime.UtcNow,
                });
                transaction.Create(_firestore.Collection(CollectionName).Document(restore.Id), restore);
                transaction.Create(_firestore.Collection(CollectionName).Document(safetyExport.Id), safetyExport);
                return true;
            });

        public Task<bool> TryTransitionAsync(string id, FirestoreBackup.BackupState expectedState, Action<FirestoreBackup> apply) =>
            _firestore.RunTransactionAsync(async transaction =>
            {
                var documentReference = _firestore.Collection(CollectionName).Document(id);
                var snapshot = await transaction.GetSnapshotAsync(documentReference);
                if (!snapshot.Exists) return false;

                var backup = snapshot.ConvertTo<FirestoreBackup>();
                if (backup.State != expectedState) return false;

                apply(backup);
                backup.UpdatedAt = DateTime.UtcNow;
                transaction.Set(documentReference, backup);
                return true;
            });

        public async Task<FirestoreBackup?> GetByIdAsync(string id)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(id).GetSnapshotAsync();
            return snapshot.Exists ? snapshot.ConvertTo<FirestoreBackup>() : null;
        }

        public async Task<IReadOnlyList<FirestoreBackup>> GetAllAsync()
        {
            var snapshot = await _firestore.Collection(CollectionName)
                .OrderByDescending(nameof(FirestoreBackup.CreatedAt))
                .GetSnapshotAsync();
            return snapshot.Documents.Select(document => document.ConvertTo<FirestoreBackup>()).ToList();
        }

        public Task UpdateAsync(FirestoreBackup backup)
        {
            backup.UpdatedAt = DateTime.UtcNow;
            return _firestore.Collection(CollectionName).Document(backup.Id).SetAsync(backup);
        }

        public Task DeleteAsync(string id) =>
            _firestore.Collection(CollectionName).Document(id).DeleteAsync();
    }
}
