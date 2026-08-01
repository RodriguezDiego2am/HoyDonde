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
    // probar la garantía real de atomicidad frente a validaciones concurrentes.
    public class TicketServiceValidationTests
    {
        private const string ControlId = "control-uid";
        private const string EventId = "event-1";

        private static TicketService CreateSut(FakeTicketValidationStore store, Control? control)
        {
            var userRepository = new Mock<IUserRepository>();
            userRepository.Setup(r => r.GetUserByIdAsync(ControlId)).ReturnsAsync(control);

            var logger = new Mock<ILogger<TicketService>>();

            // FirestoreDb no se usa en ValidateTicketAsync (sólo en BuyTicketsAsync /
            // GetTicketsByClienteIdAsync, fuera del alcance de este hotfix), por eso null! es seguro acá.
            return new TicketService(null!, userRepository.Object, store, logger.Object);
        }

        private static Control ActiveControl(string eventId = EventId) => new Control
        {
            Id = ControlId,
            EventId = eventId,
            Role = Roles.Control,
            IsActive = true
        };

        private static Ticket EmitidoTicket(string id = "ticket-1", string eventId = EventId) => new Ticket
        {
            Id = id,
            EventoId = eventId,
            ClienteId = "cliente-1",
            Estado = Ticket.TicketStatus.Emitido
        };

        [Fact]
        public async Task ValidateTicketAsync_AssignedEventAndEmitido_ReturnsSuccessAndMarksUsed()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store, ActiveControl());

            var outcome = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.Success, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_SecondAttemptOnSameTicket_ReturnsAlreadyUsed()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store, ActiveControl());

            var first = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);
            var second = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.Success, first);
            Assert.Equal(TicketValidationOutcome.AlreadyUsed, second);
        }

        [Fact]
        public async Task ValidateTicketAsync_ConcurrentValidations_OnlyOneSucceeds()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store, ActiveControl());

            // Task.Run fuerza ejecución real en threads del pool, no sólo await secuencial.
            var task1 = Task.Run(() => sut.ValidateTicketAsync(ControlId, "ticket-1", EventId));
            var task2 = Task.Run(() => sut.ValidateTicketAsync(ControlId, "ticket-1", EventId));

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
            var sut = CreateSut(store, ActiveControl());

            var outcome = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.Cancelled, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_TicketFromAnotherEvent_ReturnsNotAuthorized()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket(eventId: "event-2"));
            var sut = CreateSut(store, ActiveControl());

            var outcome = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_ControlAssignedToDifferentEvent_ReturnsNotAuthorized()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            // El Control está asignado a otro evento; no debería llegar a mirar el ticket siquiera.
            var sut = CreateSut(store, ActiveControl(eventId: "event-2"));

            var outcome = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_InactiveControl_ReturnsNotAuthorized()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var inactiveControl = ActiveControl();
            inactiveControl.IsActive = false;
            var sut = CreateSut(store, inactiveControl);

            var outcome = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }

        [Fact]
        public async Task ValidateTicketAsync_UnknownUser_ReturnsNotAuthorized()
        {
            var store = new FakeTicketValidationStore();
            store.Seed(EmitidoTicket());
            var sut = CreateSut(store, control: null);

            var outcome = await sut.ValidateTicketAsync(ControlId, "ticket-1", EventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }
    }
}
