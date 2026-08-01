using System;
using System.Threading.Tasks;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using HoyDonde.API.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Ejercita TicketService.ValidateTicketAsync directamente (sin HTTP/TestApplicationFactory),
    // porque TicketsControllerTests mockea ITicketService por completo y por lo tanto no puede
    // probar la garantía real de atomicidad frente a validaciones concurrentes, ni la resolución
    // UID -> ControlPersonaId -> control_asignaciones (docs/security-refactor-plan.md §4, Etapa 4).
    public class TicketServiceValidationTests
    {
        private const string ControlUid = "control-uid";
        private const string ControlPersonaId = "control-persona-1";
        private const string EventId = "event-1";

        private static TicketService CreateSut(
            FakeTicketValidationStore store,
            bool controlProvisioned = true,
            bool asignado = true)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            if (controlProvisioned)
            {
                personaResolver.Setup(r => r.ResolvePersonaIdAsync(ControlUid)).ReturnsAsync(ControlPersonaId);
            }
            else
            {
                personaResolver
                    .Setup(r => r.ResolvePersonaIdAsync(ControlUid))
                    .ThrowsAsync(new HoyDonde.API.Exceptions.IdentityNotProvisionedException(ControlUid));
            }

            var controlAsignacionRepository = new Mock<IControlAsignacionRepository>();
            controlAsignacionRepository
                .Setup(r => r.ExisteAsignacionAsync(ControlPersonaId, EventId))
                .ReturnsAsync(asignado);

            var logger = new Mock<ILogger<TicketService>>();

            // FirestoreDb no se usa en ValidateTicketAsync (sólo en BuyTicketsAsync /
            // GetTicketsByClienteIdAsync, fuera del alcance de este archivo), por eso null! es seguro acá.
            return new TicketService(null!, personaResolver.Object, controlAsignacionRepository.Object, store, logger.Object);
        }

        private static Ticket EmitidoTicket(string id = "ticket-1", string eventId = EventId) => new Ticket
        {
            Id = id,
            EventoId = eventId,
            ClientePersonaId = "cliente-persona-1",
            Estado = Ticket.TicketStatus.Emitido
        };

        [Fact]
        public async Task ValidateTicketAsync_AssignedEventAndEmitido_ReturnsSuccessAndMarksUsed()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store);

            var outcome = await sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.Success, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_SecondAttemptOnSameTicket_ReturnsAlreadyUsed()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store);

            var first = await sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId);
            var second = await sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.Success, first);
            Assert.Equal(TicketValidationOutcome.AlreadyUsed, second);
        }

        [Fact]
        public async Task ValidateTicketAsync_ConcurrentValidations_OnlyOneSucceeds()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store);

            // Task.Run fuerza ejecución real en threads del pool, no sólo await secuencial.
            var task1 = Task.Run(() => sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId));
            var task2 = Task.Run(() => sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId));

            var results = await Task.WhenAll(task1, task2);

            Assert.Single(results, r => r == TicketValidationOutcome.Success);
            Assert.Single(results, r => r == TicketValidationOutcome.AlreadyUsed);
        }

        [Fact]
        public async Task ValidateTicketAsync_CancelledTicket_ReturnsCancelled()
        {
            var store = new FakeTicketValidationStore();
            var ticket = EmitidoTicket();
            ticket.Estado = Ticket.TicketStatus.Anulado;
            store.Seed(ticket);
            var sut = CreateSut(store);

            var outcome = await sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.Cancelled, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_TicketFromAnotherEvent_ReturnsNotAuthorized()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket(eventId: "event-2"));
            var sut = CreateSut(store);

            var outcome = await sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_ControlNotAssignedToEvent_ReturnsNotAuthorized()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            // El Control no tiene control_asignaciones/{ControlPersonaId}_{EventId}: ni siquiera
            // debería llegar a mirar el ticket.
            var sut = CreateSut(store, asignado: false);

            var outcome = await sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_ControlAssignedToTwoOrMoreEvents_CanValidateEach()
        {
            const string otherEventId = "event-2";
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket(id: "ticket-1", eventId: EventId));
            store.Seed(EmitidoTicket(id: "ticket-2", eventId: otherEventId));

            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            personaResolver.Setup(r => r.ResolvePersonaIdAsync(ControlUid)).ReturnsAsync(ControlPersonaId);

            var controlAsignacionRepository = new Mock<IControlAsignacionRepository>();
            controlAsignacionRepository.Setup(r => r.ExisteAsignacionAsync(ControlPersonaId, EventId)).ReturnsAsync(true);
            controlAsignacionRepository.Setup(r => r.ExisteAsignacionAsync(ControlPersonaId, otherEventId)).ReturnsAsync(true);

            var logger = new Mock<ILogger<TicketService>>();
            var sut = new TicketService(null!, personaResolver.Object, controlAsignacionRepository.Object, store, logger.Object);

            var outcomeEvent1 = await sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId);
            var outcomeEvent2 = await sut.ValidateTicketAsync(ControlUid, "ticket-2", otherEventId);

            Assert.Equal(TicketValidationOutcome.Success, outcomeEvent1);
            Assert.Equal(TicketValidationOutcome.Success, outcomeEvent2);
        }

        [Fact]
        public async Task ValidateTicketAsync_ActorNotProvisioned_PropagatesIdentityNotProvisionedException()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store, controlProvisioned: false);

            await Assert.ThrowsAsync<HoyDonde.API.Exceptions.IdentityNotProvisionedException>(
                () => sut.ValidateTicketAsync(ControlUid, "ticket-1", EventId));
        }
    }
}
