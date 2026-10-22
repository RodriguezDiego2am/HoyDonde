using System.Threading.Tasks;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    // External managed Firestore API boundary. It is intentionally independent from FirestoreDb so
    // service state transitions can be tested without a Firestore Emulator or Google Cloud.
    public interface IManagedFirestoreBackupClient
    {
        Task<ManagedFirestoreOperation> StartExportAsync(string outputUriPrefix);
        Task<ManagedFirestoreOperation> StartImportAsync(string inputUriPrefix);
        Task<ManagedFirestoreOperation> GetOperationAsync(string operationName, FirestoreBackup.BackupOperation operation);
        Task DeleteExportObjectsAsync(string storageUri);
    }

    public sealed class ManagedFirestoreOperation
    {
        public string Name { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public bool IsFailed { get; init; }
    }
}
