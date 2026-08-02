using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // API-MVP 3 (docs/api-mvp-plan.md §4) ejercitado contra Firestore Emulator real:
    // UserService.AsignarControlExistenteAsync con FirestoreUsuarioRepository/
    // FirestoreControlAsignacionRepository/EventService reales, para probar de punta a punta la
    // resolución PersonaId->Usuario, el chequeo de rol CONTROL activo, el ámbito del organizador
    // y la idempotencia/concurrencia a nivel de servicio (no solo a nivel repositorio, ya
    // cubierto por FirestoreControlAsignacionRepositoryTests). IIdentityProvider/
    // IIdentidadHuerfanaRepository quedan mockeados: ninguna asignación de Control existente
    // debe tocarlos.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class UserServiceControlAssignmentEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public UserServiceControlAssignmentEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private UserService CreateSut(Mock<IIdentityProvider> identityProvider, params (string uid, string personaId)[] actors)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            foreach (var (uid, personaId) in actors)
            {
                personaResolver.Setup(r => r.ResolvePersonaIdAsync(uid)).ReturnsAsync(personaId);
            }

            var eventService = new EventService(_fixture.Db!, personaResolver.Object, Mock.Of<ILogger<EventService>>());
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);

            return new UserService(
                usuarioRepository,
                Mock.Of<IIdentidadHuerfanaRepository>(),
                eventService,
                identityProvider.Object,
                personaResolver.Object,
                controlAsignacionRepository,
                Mock.Of<ILogger<UserService>>());
        }

        private async Task<Event> SeedEventAsync(
            string organizadorPersonaId,
            Event.EventStatus estado = Event.EventStatus.Borrador,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null)
        {
            var evento = new Event
            {
                Id = $"event-{Guid.NewGuid():N}",
                Nombre = "Evento de prueba",
                Descripcion = "Descripcion",
                Ubicacion = "Buenos Aires",
                Categoria = Event.EventCategory.Musica,
                FechaInicio = fechaInicio ?? DateTime.UtcNow.AddDays(5),
                FechaFin = fechaFin ?? DateTime.UtcNow.AddDays(6),
                OrganizadorPersonaId = organizadorPersonaId,
                Estado = estado,
            };
            await _fixture.Db!.Collection("events").Document(evento.Id).SetAsync(evento);
            return evento;
        }

        private async Task SeedControlUsuarioAsync(string personaId, bool activo = true, bool conRolControlActivo = true)
        {
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = $"usuario-{Guid.NewGuid():N}";
            await usuarioRepository.ProvisionarAsync(new UsuarioProvisioningRequest(
                personaId, usuarioId, "FIREBASE", $"uid-control-{Guid.NewGuid():N}", "control@test.com", "CONTROL", "test"));

            if (!activo)
            {
                await _fixture.Db!.Collection("usuarios").Document(usuarioId).UpdateAsync(nameof(Usuario.IsActive), false);
            }

            if (!conRolControlActivo)
            {
                await _fixture.Db!.Collection("usuarios").Document(usuarioId)
                    .Collection("roles").Document("CONTROL").UpdateAsync(nameof(UsuarioRol.Activo), false);
            }
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_ForSecondOwnEvent_Succeeds_AndPersists_WithoutTouchingIdentityProvider()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var identityProvider = new Mock<IIdentityProvider>();
            var sut = CreateSut(identityProvider, (organizadorUid, organizadorPersonaId));

            var eventoA = await SeedEventAsync(organizadorPersonaId);
            var eventoB = await SeedEventAsync(organizadorPersonaId);
            await SeedControlUsuarioAsync(controlPersonaId);

            // Ámbito: el Control ya tiene una asignación previa de este organizador en eventoA.
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventoA.Id, organizadorPersonaId);

            var resultado = await sut.AsignarControlExistenteAsync(organizadorUid, eventoB.Id, controlPersonaId);

            Assert.Equal(controlPersonaId, resultado.ControlPersonaId);
            Assert.Equal(eventoB.Id, resultado.EventId);
            Assert.Equal(organizadorPersonaId, resultado.AssignedByPersonaId);
            Assert.True(await controlAsignacionRepository.ExisteAsignacionAsync(controlPersonaId, eventoB.Id));
            identityProvider.Verify(p => p.CreateIdentityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_CalledConcurrently_ForSamePair_NoneFail_AndOnlyOneAssignmentSurvives()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var eventoA = await SeedEventAsync(organizadorPersonaId);
            var eventoB = await SeedEventAsync(organizadorPersonaId);
            await SeedControlUsuarioAsync(controlPersonaId);

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventoA.Id, organizadorPersonaId);

            const int concurrency = 8;
            var tasks = Enumerable.Range(0, concurrency)
                .Select(_ => sut.AsignarControlExistenteAsync(organizadorUid, eventoB.Id, controlPersonaId))
                .ToArray();

            var overallException = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
            Assert.Null(overallException);

            var asignacionFinal = await controlAsignacionRepository.GetAsignacionAsync(controlPersonaId, eventoB.Id);
            Assert.NotNull(asignacionFinal);
            foreach (var task in tasks)
            {
                Assert.Equal(asignacionFinal!.CreatedAt, task.Result.CreatedAt);
                Assert.Equal(asignacionFinal.AssignedByPersonaId, task.Result.AssignedByPersonaId);
            }
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_PersonaInexistente_ThrowsControlInvalidoException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId);

            await Assert.ThrowsAsync<ControlInvalidoException>(
                () => sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, $"persona-inexistente-{Guid.NewGuid():N}"));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_UsuarioInactivo_ThrowsControlInvalidoException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId);
            await SeedControlUsuarioAsync(controlPersonaId, activo: false);
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, $"otro-event-{Guid.NewGuid():N}", organizadorPersonaId);

            await Assert.ThrowsAsync<ControlInvalidoException>(
                () => sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, controlPersonaId));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_SinRolControlActivo_ThrowsControlInvalidoException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId);
            await SeedControlUsuarioAsync(controlPersonaId, conRolControlActivo: false);
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, $"otro-event-{Guid.NewGuid():N}", organizadorPersonaId);

            await Assert.ThrowsAsync<ControlInvalidoException>(
                () => sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, controlPersonaId));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_ControlAdministradoPorOtroOrganizador_ThrowsControlAjenoException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId);
            await SeedControlUsuarioAsync(controlPersonaId);
            // Asignación previa existe, pero fue creada por OTRO organizador: no habilita el ámbito.
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, $"otro-event-{Guid.NewGuid():N}", "persona-otro-organizador");

            await Assert.ThrowsAsync<ControlAjenoException>(
                () => sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, controlPersonaId));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_EventoCancelado_ThrowsEventoNoDisponibleException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId, estado: Event.EventStatus.Cancelado);
            await SeedControlUsuarioAsync(controlPersonaId);
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, $"otro-event-{Guid.NewGuid():N}", organizadorPersonaId);

            await Assert.ThrowsAsync<EventoNoDisponibleParaAsignacionControlException>(
                () => sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, controlPersonaId));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_EventoFinalizado_ThrowsEventoNoDisponibleException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(
                organizadorPersonaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-2),
                fechaFin: DateTime.UtcNow.AddDays(-1));
            await SeedControlUsuarioAsync(controlPersonaId);
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, $"otro-event-{Guid.NewGuid():N}", organizadorPersonaId);

            await Assert.ThrowsAsync<EventoNoDisponibleParaAsignacionControlException>(
                () => sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, controlPersonaId));
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_EventoPublicadoEnCurso_Succeeds()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(
                organizadorPersonaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddHours(-1),
                fechaFin: DateTime.UtcNow.AddHours(1));
            await SeedControlUsuarioAsync(controlPersonaId);
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, $"otro-event-{Guid.NewGuid():N}", organizadorPersonaId);

            var resultado = await sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, controlPersonaId);

            Assert.Equal(evento.Id, resultado.EventId);
        }

        [FirestoreEmulatorFact]
        public async Task AsignarControlExistenteAsync_ForeignEvent_ThrowsOwnershipException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var otroOrganizadorPersonaId = $"persona-otro-org-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut(new Mock<IIdentityProvider>(), (organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(otroOrganizadorPersonaId);
            await SeedControlUsuarioAsync(controlPersonaId);

            await Assert.ThrowsAsync<EventOwnershipException>(
                () => sut.AsignarControlExistenteAsync(organizadorUid, evento.Id, controlPersonaId));
        }
    }
}
