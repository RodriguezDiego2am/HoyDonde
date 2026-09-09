using System;
using Google.Cloud.Firestore;

namespace HoyDonde.API.Models
{
    /// <summary>
    /// Metadata for a managed Firestore export or import. The actual document data remains in the
    /// managed Firestore export in Cloud Storage; this collection never serializes application data.
    /// </summary>
    [FirestoreData]
    public class FirestoreBackup
    {
        public enum BackupKind { Manual, AutomaticPreRestore, Restore }
        public enum BackupOperation { Export, Import }
        public enum BackupState { Pending, Running, Succeeded, Failed }

        [FirestoreDocumentId]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [FirestoreProperty]
        public BackupKind Kind { get; set; }

        [FirestoreProperty]
        public BackupOperation Operation { get; set; }

        [FirestoreProperty]
        public BackupState State { get; set; } = BackupState.Pending;

        // Internal only: never included in FirestoreBackupResponseDto.
        [FirestoreProperty]
        public string StorageUri { get; set; } = string.Empty;

        // Google long-running-operation name; internal only.
        [FirestoreProperty]
        public string? ManagedOperationName { get; set; }

        // For a restore record, identifies the successful export selected by the administrator.
        [FirestoreProperty]
        public string? SourceBackupId { get; set; }

        // For the automatic pre-restore export, identifies the pending restore to start after this
        // safety export has completed successfully.
        [FirestoreProperty]
        public string? ContinueRestoreId { get; set; }

        [FirestoreProperty]
        public string RequestedByUsuarioId { get; set; } = string.Empty;

        [FirestoreProperty]
        public string RequestedByPersonaId { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [FirestoreProperty]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [FirestoreProperty]
        public DateTime? CompletedAt { get; set; }

        // Kept solely for server logs/operations. It is deliberately not returned by API DTOs.
        [FirestoreProperty]
        public string? InternalError { get; set; }
    }
}
