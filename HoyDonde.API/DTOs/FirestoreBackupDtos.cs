using System;
using System.ComponentModel.DataAnnotations;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Public history deliberately exposes only lifecycle state; cloud/provider failures never leave
    // the API through this contract.
    public class FirestoreBackupResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public FirestoreBackup.BackupKind Kind { get; set; }
        public FirestoreBackup.BackupOperation Operation { get; set; }
        public FirestoreBackup.BackupState State { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public class DeleteFirestoreBackupRequestDto
    {
        // Deliberately not [Required]: with [ApiController], a Required bool? turns an omitted
        // confirmation into a generic 400 VALIDATION_ERROR before the service ever runs, which
        // makes BACKUP_DELETE_NOT_CONFIRMED unreachable and hides why the request was refused.
        // Omission binds as false and is rejected by the service, which owns this decision.
        public bool? ConfirmarEliminacion { get; set; }
    }

    // La eliminación devuelve un DTO como cualquier otro borde HTTP, nunca un objeto anónimo.
    public class DeleteFirestoreBackupResponseDto
    {
        public string Message { get; set; } = string.Empty;
    }
}
