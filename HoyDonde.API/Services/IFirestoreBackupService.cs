using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;

namespace HoyDonde.API.Services
{
    public interface IFirestoreBackupService
    {
        Task<FirestoreBackupResponseDto> CreateManualAsync(string actorExternalSubjectId);
        Task<IReadOnlyList<FirestoreBackupResponseDto>> ListAsync();
        Task<FirestoreBackupResponseDto> RestoreAsync(string actorExternalSubjectId, string backupId);
        Task DeleteAsync(string actorExternalSubjectId, string backupId, bool confirmarEliminacion);
    }
}
