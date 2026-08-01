using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Ejercita UserService directamente (sin HTTP/TestApplicationFactory): altas de
    // Admin/Organizador/Control sobre el modelo nuevo (Persona+Usuario+UsuarioRol+
    // IdentidadExterna vía IUsuarioRepository), incluida la compensación simplificada de
    // docs/security-refactor-plan.md §2.2, Etapa 3.
    public class UserServiceProvisioningTests
    {
        private const string ActorUid = "actor-uid-1";
        private const string EventId = "event-1";

        private static (
            UserService sut,
            Mock<IUsuarioRepository> usuarioRepository,
            Mock<IIdentidadHuerfanaRepository> identidadHuerfanaRepository,
            Mock<IEventService> eventService,
            Mock<IIdentityProvider> identityProvider,
            Mock<ILogger<UserService>> logger) CreateSut()
        {
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var identidadHuerfanaRepository = new Mock<IIdentidadHuerfanaRepository>();
            var eventService = new Mock<IEventService>();
            var identityProvider = new Mock<IIdentityProvider>();
            var logger = new Mock<ILogger<UserService>>();

            var sut = new UserService(
                usuarioRepository.Object,
                identidadHuerfanaRepository.Object,
                eventService.Object,
                identityProvider.Object,
                logger.Object);

            return (sut, usuarioRepository, identidadHuerfanaRepository, eventService, identityProvider, logger);
        }

        // ---- Happy path: Admin/Organizador ----

        [Fact]
        public async Task RegisterAdminAsync_HappyPath_UsesActorAsAssignedBy_AndNeverCompensates()
        {
            var (sut, usuarioRepository, identidadHuerfanaRepository, _, identityProvider, _) = CreateSut();
            identityProvider
                .Setup(p => p.CreateIdentityAsync("admin@test.com", "Password123!", null))
                .ReturnsAsync(new IdentityCreationResult("uid-admin", FirebaseIdentityProvider.ProviderName));

            UsuarioProvisioningRequest? capturedRequest = null;
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .Callback<UsuarioProvisioningRequest>(r => capturedRequest = r)
                .ReturnsAsync(new UsuarioProvisioningResult("persona-1", "usuario-1"));

            var result = await sut.RegisterAdminAsync(ActorUid, "admin@test.com", "Password123!");

            Assert.Equal("persona-1", result.PersonaId);
            Assert.Equal("usuario-1", result.UsuarioId);
            Assert.NotNull(capturedRequest);
            Assert.Equal("ADMINISTRADOR", capturedRequest!.RolCodigo);
            Assert.Equal(ActorUid, capturedRequest.AssignedBy);
            Assert.Equal("uid-admin", capturedRequest.ExternalSubjectId);

            identityProvider.Verify(p => p.SetTemporaryClaimAsync("uid-admin",
                It.Is<System.Collections.Generic.IReadOnlyDictionary<string, object>>(c => (string)c["role"] == Roles.Admin)), Times.Once);
            identityProvider.Verify(p => p.DeleteIdentityAsync(It.IsAny<string>()), Times.Never);
            identidadHuerfanaRepository.Verify(r => r.RegistrarAsync(It.IsAny<IdentidadHuerfana>()), Times.Never);
        }

        [Fact]
        public async Task RegisterOrganizadorAsync_HappyPath_UsesActorAsAssignedBy()
        {
            var (sut, usuarioRepository, _, _, identityProvider, _) = CreateSut();
            identityProvider
                .Setup(p => p.CreateIdentityAsync("org@test.com", "Password123!", null))
                .ReturnsAsync(new IdentityCreationResult("uid-org", FirebaseIdentityProvider.ProviderName));

            UsuarioProvisioningRequest? capturedRequest = null;
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .Callback<UsuarioProvisioningRequest>(r => capturedRequest = r)
                .ReturnsAsync(new UsuarioProvisioningResult("persona-2", "usuario-2"));

            await sut.RegisterOrganizadorAsync(ActorUid, "org@test.com", "Password123!");

            Assert.Equal("ORGANIZADOR", capturedRequest!.RolCodigo);
            Assert.Equal(ActorUid, capturedRequest.AssignedBy);
            identityProvider.Verify(p => p.SetTemporaryClaimAsync("uid-org",
                It.Is<System.Collections.Generic.IReadOnlyDictionary<string, object>>(c => (string)c["role"] == Roles.Organizador)), Times.Once);
        }

        // ---- Control: ownership antes de tocar Firebase ----

        [Fact]
        public async Task RegisterControlAsync_ForOwnEvent_Succeeds()
        {
            var (sut, usuarioRepository, _, eventService, identityProvider, _) = CreateSut();
            eventService.Setup(s => s.GetByIdAsync(EventId)).ReturnsAsync(new Event { Id = EventId, OrganizadorId = ActorUid });
            identityProvider
                .Setup(p => p.CreateIdentityAsync("control1@control.hoydonde.com", "Password123!", "control1"))
                .ReturnsAsync(new IdentityCreationResult("uid-control", FirebaseIdentityProvider.ProviderName));
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .ReturnsAsync(new UsuarioProvisioningResult("persona-3", "usuario-3"));

            var ex = await Record.ExceptionAsync(() => sut.RegisterControlAsync(ActorUid, "control1", "Password123!", EventId));

            Assert.Null(ex);
            usuarioRepository.Verify(r => r.ProvisionarAsync(It.Is<UsuarioProvisioningRequest>(req => req.RolCodigo == "CONTROL" && req.AssignedBy == ActorUid)), Times.Once);
        }

        [Fact]
        public async Task RegisterControlAsync_ForForeignEvent_ThrowsOwnershipException_AndNeverTouchesIdentityProvider()
        {
            var (sut, usuarioRepository, _, eventService, identityProvider, _) = CreateSut();
            eventService.Setup(s => s.GetByIdAsync(EventId)).ReturnsAsync(new Event { Id = EventId, OrganizadorId = "otro-organizador" });

            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.RegisterControlAsync(ActorUid, "control1", "Password123!", EventId));

            identityProvider.Verify(p => p.CreateIdentityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
            usuarioRepository.Verify(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()), Times.Never);
        }

        [Fact]
        public async Task RegisterControlAsync_ForNonexistentEvent_ThrowsNotFoundException_AndNeverTouchesIdentityProvider()
        {
            var (sut, usuarioRepository, _, eventService, identityProvider, _) = CreateSut();
            eventService.Setup(s => s.GetByIdAsync(EventId)).ReturnsAsync((Event?)null);

            await Assert.ThrowsAsync<EventNotFoundException>(() => sut.RegisterControlAsync(ActorUid, "control1", "Password123!", EventId));

            identityProvider.Verify(p => p.CreateIdentityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
            usuarioRepository.Verify(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()), Times.Never);
        }

        // ---- Email ya existe: 409 sin compensar, sin tocar Firestore ----

        [Fact]
        public async Task RegisterAdminAsync_WhenEmailAlreadyExists_PropagatesWithoutCompensating()
        {
            var (sut, usuarioRepository, identidadHuerfanaRepository, _, identityProvider, _) = CreateSut();
            identityProvider
                .Setup(p => p.CreateIdentityAsync("duplicado@test.com", "Password123!", null))
                .ThrowsAsync(new IdentityEmailAlreadyExistsException("duplicado@test.com"));

            await Assert.ThrowsAsync<IdentityEmailAlreadyExistsException>(
                () => sut.RegisterAdminAsync(ActorUid, "duplicado@test.com", "Password123!"));

            identityProvider.Verify(p => p.DeleteIdentityAsync(It.IsAny<string>()), Times.Never);
            usuarioRepository.Verify(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()), Times.Never);
            identidadHuerfanaRepository.Verify(r => r.RegistrarAsync(It.IsAny<IdentidadHuerfana>()), Times.Never);
        }

        // ---- Compensación ----

        [Fact]
        public async Task RegisterAdminAsync_WhenSetTemporaryClaimFails_CompensatesByDeletingIdentity()
        {
            var (sut, usuarioRepository, identidadHuerfanaRepository, _, identityProvider, _) = CreateSut();
            identityProvider
                .Setup(p => p.CreateIdentityAsync("admin@test.com", "Password123!", null))
                .ReturnsAsync(new IdentityCreationResult("uid-admin", FirebaseIdentityProvider.ProviderName));
            var claimError = new InvalidOperationException("claim failed");
            identityProvider
                .Setup(p => p.SetTemporaryClaimAsync("uid-admin", It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, object>>()))
                .ThrowsAsync(claimError);
            identityProvider.Setup(p => p.DeleteIdentityAsync("uid-admin")).Returns(Task.CompletedTask);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.RegisterAdminAsync(ActorUid, "admin@test.com", "Password123!"));

            Assert.Same(claimError, thrown);
            identityProvider.Verify(p => p.DeleteIdentityAsync("uid-admin"), Times.Once);
            usuarioRepository.Verify(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()), Times.Never);
            identidadHuerfanaRepository.Verify(r => r.RegistrarAsync(It.IsAny<IdentidadHuerfana>()), Times.Never);
        }

        [Fact]
        public async Task RegisterAdminAsync_WhenProvisionarAsyncFails_CompensatesByDeletingIdentity()
        {
            var (sut, usuarioRepository, identidadHuerfanaRepository, _, identityProvider, _) = CreateSut();
            identityProvider
                .Setup(p => p.CreateIdentityAsync("admin@test.com", "Password123!", null))
                .ReturnsAsync(new IdentityCreationResult("uid-admin", FirebaseIdentityProvider.ProviderName));
            var firestoreError = new InvalidOperationException("firestore failed");
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .ThrowsAsync(firestoreError);
            identityProvider.Setup(p => p.DeleteIdentityAsync("uid-admin")).Returns(Task.CompletedTask);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.RegisterAdminAsync(ActorUid, "admin@test.com", "Password123!"));

            Assert.Same(firestoreError, thrown);
            identityProvider.Verify(p => p.DeleteIdentityAsync("uid-admin"), Times.Once);
            identidadHuerfanaRepository.Verify(r => r.RegistrarAsync(It.IsAny<IdentidadHuerfana>()), Times.Never);
        }

        [Fact]
        public async Task RegisterAdminAsync_WhenCompensationFails_RegistersIdentidadHuerfana_WithBothErrors()
        {
            var (sut, usuarioRepository, identidadHuerfanaRepository, _, identityProvider, _) = CreateSut();
            identityProvider
                .Setup(p => p.CreateIdentityAsync("admin@test.com", "Password123!", null))
                .ReturnsAsync(new IdentityCreationResult("uid-admin", FirebaseIdentityProvider.ProviderName));
            var firestoreError = new InvalidOperationException("firestore failed");
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .ThrowsAsync(firestoreError);
            var compensationError = new InvalidOperationException("delete failed");
            identityProvider.Setup(p => p.DeleteIdentityAsync("uid-admin")).ThrowsAsync(compensationError);

            IdentidadHuerfana? registered = null;
            identidadHuerfanaRepository
                .Setup(r => r.RegistrarAsync(It.IsAny<IdentidadHuerfana>()))
                .Callback<IdentidadHuerfana>(h => registered = h)
                .Returns(Task.CompletedTask);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.RegisterAdminAsync(ActorUid, "admin@test.com", "Password123!"));

            Assert.Same(firestoreError, thrown);
            Assert.NotNull(registered);
            Assert.Equal("uid-admin", registered!.ExternalSubjectId);
            Assert.Equal(FirebaseIdentityProvider.ProviderName, registered.IdentityProvider);
            Assert.Equal("ADMINISTRADOR", registered.RolCodigoSolicitado);
            Assert.Contains("firestore failed", registered.ErrorOriginal);
            Assert.Contains("delete failed", registered.ErrorCompensacion);
        }

        [Fact]
        public async Task RegisterAdminAsync_WhenCompensationAndHuerfanaRegistrationBothFail_LogsStructuredErrors_AndStillThrowsOriginal()
        {
            var (sut, usuarioRepository, identidadHuerfanaRepository, _, identityProvider, logger) = CreateSut();
            identityProvider
                .Setup(p => p.CreateIdentityAsync("admin@test.com", "Password123!", null))
                .ReturnsAsync(new IdentityCreationResult("uid-admin", FirebaseIdentityProvider.ProviderName));
            var firestoreError = new InvalidOperationException("firestore failed");
            usuarioRepository
                .Setup(r => r.ProvisionarAsync(It.IsAny<UsuarioProvisioningRequest>()))
                .ThrowsAsync(firestoreError);
            identityProvider.Setup(p => p.DeleteIdentityAsync("uid-admin")).ThrowsAsync(new InvalidOperationException("delete failed"));
            identidadHuerfanaRepository
                .Setup(r => r.RegistrarAsync(It.IsAny<IdentidadHuerfana>()))
                .ThrowsAsync(new InvalidOperationException("huerfana registration failed"));

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.RegisterAdminAsync(ActorUid, "admin@test.com", "Password123!"));

            Assert.Same(firestoreError, thrown);

            logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Exactly(3));
        }
    }
}
