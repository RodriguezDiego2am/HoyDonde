using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.Controllers;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class BackupsControllerTests
    {
        [Fact]
        public async Task CrearManual_UsesAuthenticatedUid_AndReturnsAccepted()
        {
            var service = new Mock<IFirestoreBackupService>();
            service.Setup(s => s.CreateManualAsync("uid-backup-admin"))
                .ReturnsAsync(new FirestoreBackupResponseDto { Id = "backup-1", State = FirestoreBackup.BackupState.Running });
            var controller = CreateController(service.Object, "uid-backup-admin");

            var result = await controller.CrearManual();

            var accepted = Assert.IsType<AcceptedResult>(result);
            Assert.IsType<FirestoreBackupResponseDto>(accepted.Value);
            service.Verify(s => s.CreateManualAsync("uid-backup-admin"), Times.Once);
        }

        [Fact]
        public async Task Eliminar_PassesExplicitConfirmationToService()
        {
            var service = new Mock<IFirestoreBackupService>();
            var controller = CreateController(service.Object, "uid-backup-admin");

            var result = await controller.Eliminar("backup-1", new DeleteFirestoreBackupRequestDto { ConfirmarEliminacion = true });

            Assert.IsType<OkObjectResult>(result);
            service.Verify(s => s.DeleteAsync("uid-backup-admin", "backup-1", true), Times.Once);
        }

        [Fact]
        public async Task Listar_ReturnsTheHistoryAsRead()
        {
            var service = new Mock<IFirestoreBackupService>();
            service.Setup(s => s.ListAsync()).ReturnsAsync(new List<FirestoreBackupResponseDto>
            {
                new() { Id = "backup-1", State = FirestoreBackup.BackupState.Succeeded },
            });
            var controller = CreateController(service.Object, "uid-backup-admin");

            var result = await controller.Listar();

            var ok = Assert.IsType<OkObjectResult>(result);
            var backups = Assert.IsAssignableFrom<IReadOnlyList<FirestoreBackupResponseDto>>(ok.Value);
            Assert.Equal("backup-1", Assert.Single(backups).Id);
        }

        [Fact]
        public async Task Restaurar_UsesAuthenticatedUid_AndReturnsAccepted()
        {
            var service = new Mock<IFirestoreBackupService>();
            service.Setup(s => s.RestoreAsync("uid-backup-admin", "backup-1"))
                .ReturnsAsync(new FirestoreBackupResponseDto { Id = "restore-1", State = FirestoreBackup.BackupState.Pending });
            var controller = CreateController(service.Object, "uid-backup-admin");

            var result = await controller.Restaurar("backup-1");

            var accepted = Assert.IsType<AcceptedResult>(result);
            Assert.IsType<FirestoreBackupResponseDto>(accepted.Value);
            // El origen viaja en la ruta, pero el actor sale siempre del token, nunca del cliente.
            service.Verify(s => s.RestoreAsync("uid-backup-admin", "backup-1"), Times.Once);
        }

        [Fact]
        public async Task Eliminar_WithOmittedConfirmation_ForwardsFalse_AndNeverAssumesConsent()
        {
            // ConfirmarEliminacion es bool? justamente para que omitirlo NO se ligue como true:
            // el borrado destruye los archivos de la copia en Cloud Storage.
            var service = new Mock<IFirestoreBackupService>();
            var controller = CreateController(service.Object, "uid-backup-admin");

            await controller.Eliminar("backup-1", new DeleteFirestoreBackupRequestDto { ConfirmarEliminacion = null });

            service.Verify(s => s.DeleteAsync("uid-backup-admin", "backup-1", false), Times.Once);
            service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), true), Times.Never);
        }

        [Fact]
        public void Controller_RequiresDedicatedBackupAction()
        {
            var attribute = Assert.Single(typeof(BackupsController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true));
            Assert.Equal(Acciones.BackupAdministrar, ((Microsoft.AspNetCore.Authorization.AuthorizeAttribute)attribute).Policy);
        }

        private static BackupsController CreateController(IFirestoreBackupService service, string uid)
        {
            var controller = new BackupsController(service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, uid) }, "test")),
                },
            };
            return controller;
        }
    }
}
