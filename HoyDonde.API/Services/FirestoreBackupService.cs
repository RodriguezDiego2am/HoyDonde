using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Services
{
    /// <summary>
    /// Orchestrates metadata and native long-running Firestore operations. Polling occurs on an
    /// administrator history read: there is deliberately no scheduler or Cloud Function.
    /// </summary>
    public class FirestoreBackupService : IFirestoreBackupService
    {
        private const string TargetTipo = "FirestoreBackup";
        private readonly IFirestoreBackupRepository _backupRepository;
        private readonly IManagedFirestoreBackupClient _managedClient;
        private readonly IUsuarioRepository _usuarioRepository;
        private readonly ISecurityAuditRepository _securityAuditRepository;
        private readonly ILogger<FirestoreBackupService> _logger;
        private readonly string _bucketName;

        public FirestoreBackupService(
            IFirestoreBackupRepository backupRepository,
            IManagedFirestoreBackupClient managedClient,
            IUsuarioRepository usuarioRepository,
            ISecurityAuditRepository securityAuditRepository,
            ILogger<FirestoreBackupService> logger,
            IConfiguration configuration)
        {
            _backupRepository = backupRepository;
            _managedClient = managedClient;
            _usuarioRepository = usuarioRepository;
            _securityAuditRepository = securityAuditRepository;
            _logger = logger;
            var projectId = configuration["Firebase:ProjectId"]
                ?? throw new InvalidOperationException("Firebase:ProjectId es obligatorio para las copias de Firestore.");
            _bucketName = configuration["FirestoreBackups:BucketName"] ?? $"{projectId}.appspot.com";
        }

        public async Task<FirestoreBackupResponseDto> CreateManualAsync(string actorExternalSubjectId)
        {
            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var backup = NewExport(FirestoreBackup.BackupKind.Manual, actor);
            await _backupRepository.CreateAsync(backup);
            await AuditAsync(backup, "BACKUP_CREATE_ATTEMPT");
            await StartExportAsync(backup, actor);
            return ToResponse(backup);
        }

        public async Task<IReadOnlyList<FirestoreBackupResponseDto>> ListAsync()
        {
            var backups = await _backupRepository.GetAllAsync();
            foreach (var backup in backups.Where(b => b.State == FirestoreBackup.BackupState.Running && !string.IsNullOrWhiteSpace(b.ManagedOperationName)))
            {
                await RefreshAsync(backup);
            }

            // Recover the continuation after a process restart between the safety export completing
            // and the import being started. There is no scheduler: an administrator's history read is
            // the deliberately explicit trigger.
            foreach (var restore in backups.Where(b => b.Operation == FirestoreBackup.BackupOperation.Import && b.State == FirestoreBackup.BackupState.Pending))
            {
                var safetyBackup = backups.FirstOrDefault(b => b.ContinueRestoreId == restore.Id);
                if (safetyBackup?.State == FirestoreBackup.BackupState.Succeeded)
                {
                    await StartPendingRestoreAsync(restore.Id);
                }
                else if (safetyBackup?.State == FirestoreBackup.BackupState.Failed)
                {
                    await FailPendingRestoreAsync(restore.Id);
                }
            }
            // Re-read so a restore started through a separate document lookup is reflected in this
            // same response rather than waiting for the next manual refresh.
            backups = await _backupRepository.GetAllAsync();
            return backups.OrderByDescending(b => b.CreatedAt).Select(ToResponse).ToList();
        }

        public async Task<FirestoreBackupResponseDto> RestoreAsync(string actorExternalSubjectId, string backupId)
        {
            if (string.IsNullOrWhiteSpace(backupId)) throw new BackupNoEncontradoException();
            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var source = await _backupRepository.GetByIdAsync(backupId);
            if (source == null) throw new BackupNoEncontradoException();
            if (source.Operation != FirestoreBackup.BackupOperation.Export || source.State != FirestoreBackup.BackupState.Succeeded)
            {
                throw new BackupNoRestaurableException();
            }

            var restore = new FirestoreBackup
            {
                Kind = FirestoreBackup.BackupKind.Restore,
                Operation = FirestoreBackup.BackupOperation.Import,
                State = FirestoreBackup.BackupState.Pending,
                StorageUri = source.StorageUri,
                SourceBackupId = source.Id,
                RequestedByUsuarioId = actor.UsuarioId,
                RequestedByPersonaId = actor.PersonaId,
            };
            var safetyBackup = NewExport(FirestoreBackup.BackupKind.AutomaticPreRestore, actor);
            safetyBackup.ContinueRestoreId = restore.Id;

            // One transaction, and a global claim. Claiming each restore document on its own would
            // still let two concurrent requests create two restores and fire two imports against
            // production: the invariant is "at most one unfinished restore", not "one claim per
            // record". Nothing is written when the claim is lost.
            if (!await _backupRepository.TryCreateRestorePairAsync(restore, safetyBackup))
            {
                throw new RestauracionEnCursoException();
            }
            await AuditAsync(safetyBackup, "BACKUP_CREATE_ATTEMPT");
            await AuditAsync(restore, "BACKUP_IMPORT_ATTEMPT");
            await StartExportAsync(safetyBackup, actor);

            // Starting the safety export may already have failed this restore, and it does so on a
            // different instance loaded inside the transaction. Report what is actually persisted:
            // the local copy still says Pending for a restore that is Failed in Firestore.
            var persisted = await _backupRepository.GetByIdAsync(restore.Id);
            return ToResponse(persisted ?? restore);
        }

        public async Task DeleteAsync(string actorExternalSubjectId, string backupId, bool confirmarEliminacion)
        {
            if (!confirmarEliminacion) throw new BackupEliminacionNoConfirmadaException();
            if (string.IsNullOrWhiteSpace(backupId)) throw new BackupNoEncontradoException();

            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var backup = await _backupRepository.GetByIdAsync(backupId);
            if (backup == null) throw new BackupNoEncontradoException();
            if (backup.Operation != FirestoreBackup.BackupOperation.Export ||
                backup.State is FirestoreBackup.BackupState.Pending or FirestoreBackup.BackupState.Running)
            {
                throw new BackupNoEliminableException();
            }

            await AuditAsync(backup, "BACKUP_DELETE_ATTEMPT", actor);
            try
            {
                await _managedClient.DeleteExportObjectsAsync(backup.StorageUri);
                await _backupRepository.DeleteAsync(backup.Id);
                await AuditAsync(backup, "BACKUP_DELETE_SUCCEEDED", actor);
            }
            catch (Exception exception)
            {
                // Si los objetos de Storage ya se borraron y lo que falló fue el metadato, la copia
                // queda vacía pero seguiría figurando como Succeeded: RestoreAsync la aceptaría y la
                // pantalla seguiría ofreciendo "Restaurar" sobre un prefijo vacío. Marcarla Failed es
                // lo que la saca de circulación; es best-effort y nunca tapa la excepción original.
                _logger.LogError(exception, "Falló la eliminación de la copia {BackupId}; se marca inutilizable para que no pueda restaurarse.", backup.Id);
                try
                {
                    await _backupRepository.TryTransitionAsync(backup.Id, FirestoreBackup.BackupState.Succeeded, record =>
                    {
                        record.State = FirestoreBackup.BackupState.Failed;
                        record.CompletedAt = DateTime.UtcNow;
                        record.InternalError = "delete-left-backup-unusable";
                    });
                }
                catch (Exception markException)
                {
                    _logger.LogError(markException, "Tampoco se pudo marcar inutilizable la copia {BackupId}; revisar a mano antes de restaurar.", backup.Id);
                }

                await AuditAsync(backup, "BACKUP_DELETE_FAILED", actor);
                throw;
            }
        }

        private FirestoreBackup NewExport(FirestoreBackup.BackupKind kind, (string UsuarioId, string PersonaId) actor)
        {
            var backup = new FirestoreBackup
            {
                Kind = kind,
                Operation = FirestoreBackup.BackupOperation.Export,
                RequestedByUsuarioId = actor.UsuarioId,
                RequestedByPersonaId = actor.PersonaId,
            };
            backup.StorageUri = $"gs://{_bucketName}/firestore-backups/{backup.Id}";
            return backup;
        }

        private async Task StartExportAsync(FirestoreBackup backup, (string UsuarioId, string PersonaId) actor)
        {
            try
            {
                var operation = await _managedClient.StartExportAsync(backup.StorageUri);
                backup.ManagedOperationName = operation.Name;
                backup.State = operation.IsCompleted
                    ? (operation.IsFailed ? FirestoreBackup.BackupState.Failed : FirestoreBackup.BackupState.Succeeded)
                    : FirestoreBackup.BackupState.Running;
                if (operation.IsCompleted) backup.CompletedAt = DateTime.UtcNow;
                await _backupRepository.UpdateAsync(backup);
                if (operation.IsCompleted) await AuditOutcomeAsync(backup, operation.IsFailed, actor);
                if (backup.State == FirestoreBackup.BackupState.Succeeded && !string.IsNullOrWhiteSpace(backup.ContinueRestoreId))
                {
                    await StartPendingRestoreAsync(backup.ContinueRestoreId);
                }
            }
            catch (Exception exception)
            {
                // InternalError is a stable slug for the operator's history; the actual cause (an
                // IAM denial, a missing bucket, an expired credential) only ever exists here, so it
                // has to reach the log or nobody can diagnose a failed backup.
                _logger.LogError(exception, "No se pudo iniciar la exportación administrada {BackupId} sobre {StorageUri}.", backup.Id, backup.StorageUri);
                backup.State = FirestoreBackup.BackupState.Failed;
                backup.CompletedAt = DateTime.UtcNow;
                backup.InternalError = "managed-export-start-failed";
                await _backupRepository.UpdateAsync(backup);
                await AuditOutcomeAsync(backup, failed: true, actor);
                if (!string.IsNullOrWhiteSpace(backup.ContinueRestoreId))
                {
                    await FailPendingRestoreAsync(backup.ContinueRestoreId);
                }
            }
        }

        private async Task RefreshAsync(FirestoreBackup backup)
        {
            // A transient poll error is not a completed managed-operation failure; preserve Running
            // so a later administrator refresh can poll it again.
            ManagedFirestoreOperation operation;
            try
            {
                operation = await _managedClient.GetOperationAsync(backup.ManagedOperationName!, backup.Operation);
            }
            catch (Exception exception)
            {
                // Preserving Running is deliberate, but a permanent poll failure looks exactly like
                // a transient one from here: the managed client surfaces no status to tell them
                // apart. Logging every failed poll is what makes a record stuck Running for good
                // discoverable instead of invisible.
                _logger.LogWarning(exception, "Falló el sondeo de la operación administrada {ManagedOperationName} de {BackupId}; queda en Running para reintentar en el próximo refresco.", backup.ManagedOperationName, backup.Id);
                return;
            }
            if (!operation.IsCompleted) return;

            // Two administrators refreshing the history at the same instant both observe the same
            // completed operation. Only the caller that wins this transition writes the outcome and
            // its audit row, so the completion is never recorded twice.
            FirestoreBackup? settled = null;
            var won = await _backupRepository.TryTransitionAsync(backup.Id, FirestoreBackup.BackupState.Running, record =>
            {
                record.State = operation.IsFailed ? FirestoreBackup.BackupState.Failed : FirestoreBackup.BackupState.Succeeded;
                record.CompletedAt = DateTime.UtcNow;
                if (operation.IsFailed) record.InternalError = "managed-operation-failed";
                settled = record;
            });
            if (!won || settled == null) return;

            // Keep the caller's in-memory copy aligned with what was actually persisted.
            backup.State = settled.State;
            backup.CompletedAt = settled.CompletedAt;
            await AuditOutcomeAsync(settled, operation.IsFailed);

            if (settled.State == FirestoreBackup.BackupState.Succeeded && !string.IsNullOrWhiteSpace(settled.ContinueRestoreId))
            {
                await StartPendingRestoreAsync(settled.ContinueRestoreId);
            }
            else if (settled.State == FirestoreBackup.BackupState.Failed && !string.IsNullOrWhiteSpace(settled.ContinueRestoreId))
            {
                await FailPendingRestoreAsync(settled.ContinueRestoreId);
            }
        }

        private async Task StartPendingRestoreAsync(string restoreId)
        {
            var pending = await _backupRepository.GetByIdAsync(restoreId);
            if (pending == null || pending.Operation != FirestoreBackup.BackupOperation.Import) return;

            // An import overwrites production data, so "exactly one import per restore" is the
            // invariant that matters most in this module. Claiming Pending -> Running inside a
            // transaction is what makes it hold: a second caller loses the transition and returns
            // without ever reaching StartImportAsync.
            FirestoreBackup? restore = null;
            var claimed = await _backupRepository.TryTransitionAsync(restoreId, FirestoreBackup.BackupState.Pending, record =>
            {
                record.State = FirestoreBackup.BackupState.Running;
                restore = record;
            });
            if (!claimed || restore == null) return;

            try
            {
                var operation = await _managedClient.StartImportAsync(restore.StorageUri);
                restore.ManagedOperationName = operation.Name;
                restore.State = operation.IsCompleted
                    ? (operation.IsFailed ? FirestoreBackup.BackupState.Failed : FirestoreBackup.BackupState.Succeeded)
                    : FirestoreBackup.BackupState.Running;
                if (operation.IsCompleted) restore.CompletedAt = DateTime.UtcNow;
                await _backupRepository.UpdateAsync(restore);
                if (operation.IsCompleted) await AuditOutcomeAsync(restore, operation.IsFailed);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo iniciar la importación administrada de la restauración {BackupId} desde {StorageUri}.", restore.Id, restore.StorageUri);
                restore.State = FirestoreBackup.BackupState.Failed;
                restore.CompletedAt = DateTime.UtcNow;
                restore.InternalError = "managed-import-start-failed";
                await _backupRepository.UpdateAsync(restore);
                await AuditOutcomeAsync(restore, failed: true);
            }
        }

        private async Task FailPendingRestoreAsync(string restoreId)
        {
            FirestoreBackup? restore = null;
            var claimed = await _backupRepository.TryTransitionAsync(restoreId, FirestoreBackup.BackupState.Pending, record =>
            {
                record.State = FirestoreBackup.BackupState.Failed;
                record.CompletedAt = DateTime.UtcNow;
                record.InternalError = "pre-restore-export-failed";
                restore = record;
            });
            if (!claimed || restore == null) return;
            await AuditOutcomeAsync(restore, failed: true);
        }

        private Task AuditOutcomeAsync(FirestoreBackup backup, bool failed, (string UsuarioId, string PersonaId)? actor = null) =>
            AuditAsync(backup,
                backup.Operation == FirestoreBackup.BackupOperation.Export
                    ? (failed ? "BACKUP_CREATE_FAILED" : "BACKUP_CREATE_SUCCEEDED")
                    : (failed ? "BACKUP_IMPORT_FAILED" : "BACKUP_IMPORT_SUCCEEDED"),
                actor);

        private Task AuditAsync(FirestoreBackup backup, string operation, (string UsuarioId, string PersonaId)? actor = null)
        {
            var effectiveActor = actor ?? (backup.RequestedByUsuarioId, backup.RequestedByPersonaId);
            return _securityAuditRepository.RegistrarAsync(new SecurityAudit
            {
                ActorUsuarioId = effectiveActor.UsuarioId,
                ActorPersonaId = effectiveActor.PersonaId,
                Operacion = operation,
                TargetTipo = TargetTipo,
                TargetId = backup.Id,
                Detalle = string.Empty,
                Timestamp = DateTime.UtcNow,
            });
        }

        private async Task<(string UsuarioId, string PersonaId)> ResolveActorAsync(string externalSubjectId)
        {
            var usuarioId = await _usuarioRepository.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, externalSubjectId);
            if (usuarioId == null) throw new HoyDonde.API.Exceptions.IdentityNotProvisionedException(externalSubjectId);
            var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
            if (usuario == null) throw new HoyDonde.API.Exceptions.IdentityNotProvisionedException(externalSubjectId);
            return (usuario.Id, usuario.PersonaId);
        }

        private static FirestoreBackupResponseDto ToResponse(FirestoreBackup backup) => new()
        {
            Id = backup.Id,
            Kind = backup.Kind,
            Operation = backup.Operation,
            State = backup.State,
            CreatedAt = backup.CreatedAt,
            CompletedAt = backup.CompletedAt,
        };
    }
}
