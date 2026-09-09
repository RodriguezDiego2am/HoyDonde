using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging.Abstractions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class FirestoreBackupServiceTests
    {
        private const string ActorUid = "backup-admin-uid";
        private const string ActorUsuarioId = "backup-admin-user";
        private const string ActorPersonaId = "backup-admin-persona";

        private static IConfiguration Configuration() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firebase:ProjectId"] = "test-project",
                ["FirestoreBackups:BucketName"] = "test-project.appspot.com",
            })
            .Build();

        // The two transactional operations are the invariant this module leans on, so the fake
        // store honours them for real: TryTransitionAsync only applies when the record is still in
        // the expected state, exactly like the Firestore transaction it stands in for.
        private static (FirestoreBackupService Service, Mock<IFirestoreBackupRepository> Repository, Mock<IManagedFirestoreBackupClient> Managed, Mock<ISecurityAuditRepository> Audits, List<FirestoreBackup> Records) CreateSut()
        {
            var repository = new Mock<IFirestoreBackupRepository>();
            var records = new List<FirestoreBackup>();
            var managed = new Mock<IManagedFirestoreBackupClient>();
            var usuarios = new Mock<IUsuarioRepository>();
            var audits = new Mock<ISecurityAuditRepository>();
            usuarios.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, ActorUid)).ReturnsAsync(ActorUsuarioId);
            usuarios.Setup(r => r.GetByIdAsync(ActorUsuarioId)).ReturnsAsync(new Usuario { Id = ActorUsuarioId, PersonaId = ActorPersonaId, IsActive = true });
            audits.Setup(r => r.RegistrarAsync(It.IsAny<SecurityAudit>())).Returns(Task.CompletedTask);
            repository.Setup(r => r.CreateAsync(It.IsAny<FirestoreBackup>()))
                .Callback<FirestoreBackup>(backup => records.Add(backup))
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.TryCreateRestorePairAsync(It.IsAny<FirestoreBackup>(), It.IsAny<FirestoreBackup>()))
                .ReturnsAsync((FirestoreBackup restore, FirestoreBackup safety) =>
                {
                    // La guarda real es global: se pierde el claim mientras exista otra
                    // restauración sin terminar, sin importar de qué copia venga.
                    if (records.Any(b => b.Operation == FirestoreBackup.BackupOperation.Import
                        && b.State is FirestoreBackup.BackupState.Pending or FirestoreBackup.BackupState.Running))
                    {
                        return false;
                    }
                    records.Add(restore);
                    records.Add(safety);
                    return true;
                });
            repository.Setup(r => r.TryTransitionAsync(It.IsAny<string>(), It.IsAny<FirestoreBackup.BackupState>(), It.IsAny<Action<FirestoreBackup>>()))
                .ReturnsAsync((string id, FirestoreBackup.BackupState expected, Action<FirestoreBackup> apply) =>
                {
                    var record = records.SingleOrDefault(b => b.Id == id);
                    if (record == null || record.State != expected) return false;
                    apply(record);
                    return true;
                });
            repository.Setup(r => r.GetByIdAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => records.SingleOrDefault(b => b.Id == id));
            repository.Setup(r => r.GetAllAsync()).ReturnsAsync(() => records.ToList());
            repository.Setup(r => r.UpdateAsync(It.IsAny<FirestoreBackup>())).Returns(Task.CompletedTask);
            repository.Setup(r => r.DeleteAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            managed.Setup(c => c.DeleteExportObjectsAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            return (new FirestoreBackupService(repository.Object, managed.Object, usuarios.Object, audits.Object, NullLogger<FirestoreBackupService>.Instance, Configuration()), repository, managed, audits, records);
        }

        [Fact]
        public async Task CreateManualAsync_StartsNativeExport_AndReturnsOnlyRunningState()
        {
            var (service, repository, managed, audits, records) = CreateSut();
            FirestoreBackup? created = null;
            repository.Setup(r => r.CreateAsync(It.IsAny<FirestoreBackup>()))
                .Callback<FirestoreBackup>(backup => created = backup)
                .Returns(Task.CompletedTask);
            managed.Setup(c => c.StartExportAsync(It.IsAny<string>()))
                .ReturnsAsync(new ManagedFirestoreOperation { Name = "operations/export-1" });

            var result = await service.CreateManualAsync(ActorUid);

            Assert.NotNull(created);
            Assert.Equal(FirestoreBackup.BackupKind.Manual, result.Kind);
            Assert.Equal(FirestoreBackup.BackupState.Running, result.State);
            Assert.Equal("gs://test-project.appspot.com/firestore-backups/" + result.Id, created!.StorageUri);
            Assert.Null(typeof(HoyDonde.API.DTOs.FirestoreBackupResponseDto).GetProperty("InternalError"));
            audits.Verify(r => r.RegistrarAsync(It.Is<SecurityAudit>(a => a.Operacion == "BACKUP_CREATE_ATTEMPT" && a.TargetId == result.Id && a.Detalle == string.Empty)), Times.Once);
        }

        [Fact]
        public async Task RestoreAsync_WaitsForAutomaticSafetyExportBeforeStartingImport()
        {
            var (service, repository, managed, audits, records) = CreateSut();
            var source = new FirestoreBackup
            {
                Id = "source-export",
                Kind = FirestoreBackup.BackupKind.Manual,
                Operation = FirestoreBackup.BackupOperation.Export,
                State = FirestoreBackup.BackupState.Succeeded,
                StorageUri = "gs://test-project.appspot.com/firestore-backups/source-export",
            };
            records.Add(source);
            managed.Setup(c => c.StartExportAsync(It.IsAny<string>()))
                .ReturnsAsync(new ManagedFirestoreOperation { Name = "operations/safety-export" });
            managed.Setup(c => c.GetOperationAsync("operations/safety-export", FirestoreBackup.BackupOperation.Export))
                .ReturnsAsync(new ManagedFirestoreOperation { Name = "operations/safety-export", IsCompleted = true });
            managed.Setup(c => c.StartImportAsync(source.StorageUri))
                .ReturnsAsync(new ManagedFirestoreOperation { Name = "operations/import-1" });

            var restore = await service.RestoreAsync(ActorUid, source.Id);

            Assert.Equal(FirestoreBackup.BackupState.Pending, restore.State);
            managed.Verify(c => c.StartImportAsync(It.IsAny<string>()), Times.Never);

            await service.ListAsync();

            var restoreRecord = records.Single(b => b.Id == restore.Id);
            Assert.Equal(FirestoreBackup.BackupState.Running, restoreRecord.State);
            managed.Verify(c => c.StartImportAsync(source.StorageUri), Times.Once);
            audits.Verify(r => r.RegistrarAsync(It.Is<SecurityAudit>(a => a.Operacion == "BACKUP_CREATE_SUCCEEDED")), Times.Once);
        }

        [Fact]
        public async Task RestoreAsync_WhileAnotherRestoreIsUnfinished_IsRefused_AndWritesNothing()
        {
            var (service, repository, managed, audits, records) = CreateSut();
            var source = new FirestoreBackup
            {
                Id = "source-export",
                Operation = FirestoreBackup.BackupOperation.Export,
                State = FirestoreBackup.BackupState.Succeeded,
                StorageUri = "gs://test-project.appspot.com/firestore-backups/source-export",
            };
            records.Add(source);
            records.Add(new FirestoreBackup
            {
                Id = "restore-en-curso",
                Operation = FirestoreBackup.BackupOperation.Import,
                State = FirestoreBackup.BackupState.Running,
            });

            await Assert.ThrowsAsync<HoyDonde.API.Exceptions.RestauracionEnCursoException>(
                () => service.RestoreAsync(ActorUid, source.Id));

            // Ni copia de seguridad previa, ni exportación arrancada, ni registro nuevo.
            managed.Verify(c => c.StartExportAsync(It.IsAny<string>()), Times.Never);
            managed.Verify(c => c.StartImportAsync(It.IsAny<string>()), Times.Never);
            Assert.Equal(2, records.Count);
        }

        [Fact]
        public async Task ListAsync_CompletedManagedFailure_IsVisibleAsFailedWithoutTechnicalDetail()
        {
            var (service, repository, managed, audits, records) = CreateSut();
            var running = new FirestoreBackup
            {
                Id = "backup-running",
                Kind = FirestoreBackup.BackupKind.Manual,
                Operation = FirestoreBackup.BackupOperation.Export,
                State = FirestoreBackup.BackupState.Running,
                ManagedOperationName = "operations/export-failed",
            };
            records.Add(running);
            managed.Setup(c => c.GetOperationAsync("operations/export-failed", FirestoreBackup.BackupOperation.Export))
                .ReturnsAsync(new ManagedFirestoreOperation { Name = "operations/export-failed", IsCompleted = true, IsFailed = true });

            var result = await service.ListAsync();

            Assert.Equal(FirestoreBackup.BackupState.Failed, Assert.Single(result).State);
            Assert.Null(typeof(HoyDonde.API.DTOs.FirestoreBackupResponseDto).GetProperty("InternalError"));
            audits.Verify(r => r.RegistrarAsync(It.Is<SecurityAudit>(a => a.Operacion == "BACKUP_CREATE_FAILED" && a.Detalle == string.Empty)), Times.Once);
        }

        [Fact]
        public async Task ManagedClient_DeleteExportObjectsAsync_ListsPrefixAndDeletesUrlEncodedObjectNames()
        {
            var handler = new RecordingHttpMessageHandler(
                "{\"items\":[{\"name\":\"firestore-backups/backup-1/all_namespaces/output\"}]}",
                "{}");
            var client = new ManagedFirestoreBackupClient(new HttpClient(handler), Configuration());
            typeof(ManagedFirestoreBackupClient)
                .GetField("_credential", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(client, GoogleCredential.FromAccessToken("test-token"));

            await client.DeleteExportObjectsAsync("gs://test-project.appspot.com/firestore-backups/backup-1");

            Assert.Equal(new[]
            {
                "GET https://storage.googleapis.com/storage/v1/b/test-project.appspot.com/o?prefix=firestore-backups%2Fbackup-1",
                "DELETE https://storage.googleapis.com/storage/v1/b/test-project.appspot.com/o/firestore-backups%2Fbackup-1%2Fall_namespaces%2Foutput",
            }, handler.Requests);
        }

        [Fact]
        public async Task DeleteAsync_WithoutExplicitConfirmation_RejectsBeforeReadingOrDeleting()
        {
            var (service, repository, _, _, _) = CreateSut();

            await Assert.ThrowsAsync<HoyDonde.API.Exceptions.BackupEliminacionNoConfirmadaException>(
                () => service.DeleteAsync(ActorUid, "backup-1", confirmarEliminacion: false));

            repository.Verify(r => r.GetByIdAsync(It.IsAny<string>()), Times.Never);
            repository.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
        }

        private sealed class RecordingHttpMessageHandler : HttpMessageHandler
        {
            private readonly Queue<string> _responses;

            public RecordingHttpMessageHandler(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public List<string> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add($"{request.Method} {request.RequestUri!.AbsoluteUri}");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responses.Dequeue()),
                });
            }
        }

        [Fact]
        public async Task DeleteAsync_DeletesManagedExportObjectsBeforeRemovingMetadata()
        {
            var (service, repository, managed, audits, records) = CreateSut();
            var backup = new FirestoreBackup
            {
                Id = "backup-1",
                Kind = FirestoreBackup.BackupKind.Manual,
                Operation = FirestoreBackup.BackupOperation.Export,
                State = FirestoreBackup.BackupState.Succeeded,
                StorageUri = "gs://test-project.appspot.com/firestore-backups/backup-1",
            };
            var operations = new List<string>();
            repository.Setup(r => r.GetByIdAsync(backup.Id)).ReturnsAsync(backup);
            managed.Setup(c => c.DeleteExportObjectsAsync(backup.StorageUri))
                .Callback<string>(_ => operations.Add("storage"))
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.DeleteAsync(backup.Id))
                .Callback<string>(_ => operations.Add("metadata"))
                .Returns(Task.CompletedTask);

            await service.DeleteAsync(ActorUid, backup.Id, confirmarEliminacion: true);

            Assert.Equal(new[] { "storage", "metadata" }, operations);
            audits.Verify(r => r.RegistrarAsync(It.Is<SecurityAudit>(a => a.Operacion == "BACKUP_DELETE_SUCCEEDED")), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_StorageDeletionFailure_PreservesMetadataAndAuditsFailure()
        {
            var (service, repository, managed, audits, records) = CreateSut();
            var backup = new FirestoreBackup
            {
                Id = "backup-1",
                Kind = FirestoreBackup.BackupKind.Manual,
                Operation = FirestoreBackup.BackupOperation.Export,
                State = FirestoreBackup.BackupState.Succeeded,
                StorageUri = "gs://test-project.appspot.com/firestore-backups/backup-1",
            };
            repository.Setup(r => r.GetByIdAsync(backup.Id)).ReturnsAsync(backup);
            managed.Setup(c => c.DeleteExportObjectsAsync(backup.StorageUri))
                .ThrowsAsync(new System.InvalidOperationException("storage delete failed"));

            await Assert.ThrowsAsync<System.InvalidOperationException>(() => service.DeleteAsync(ActorUid, backup.Id, confirmarEliminacion: true));

            repository.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
            audits.Verify(r => r.RegistrarAsync(It.Is<SecurityAudit>(a => a.Operacion == "BACKUP_DELETE_FAILED")), Times.Once);
        }
    }
}
