using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // docs/security-refactor-plan.md §4, Etapa 4: Ticket.ClientePersonaId/ValidadoPorPersonaId
    // contra Firestore Emulator real. El actor se resuelve a PersonaId vía un
    // IAuthenticatedPersonaResolver mockeado (la resolución en sí ya está cubierta por
    // AuthenticatedPersonaResolverTests).
    [Collection(FirestoreEmulatorCollection.Name)]
    public class TicketServiceEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public TicketServiceEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static Mock<IAuthenticatedPersonaResolver> ResolverFor(params (string uid, string personaId)[] actors)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            foreach (var (uid, personaId) in actors)
            {
                personaResolver.Setup(r => r.ResolvePersonaIdAsync(uid)).ReturnsAsync(personaId);
            }
            return personaResolver;
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_PersistsClientePersonaId_NotUid()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var clientePersonaId = $"persona-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, clientePersonaId));

            var sut = new TicketService(
                _fixture.Db!,
                personaResolver.Object,
                Mock.Of<IControlAsignacionRepository>(),
                Mock.Of<ITicketValidationStore>(),
                Mock.Of<ILogger<TicketService>>());

            var eventId = $"event-{Guid.NewGuid():N}";
            var ticketTypeId = $"tipo-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento con stock",
                FechaInicio = DateTime.UtcNow.AddDays(5),
                FechaFin = DateTime.UtcNow.AddDays(6),
                Estado = Event.EventStatus.Publicado,
                TicketTypes = new System.Collections.Generic.List<TicketType>
                {
                    new TicketType { Id = ticketTypeId, Nombre = "General", Precio = 100, CantidadDisponible = 10, EventoId = eventId }
                }
            });

            var tickets = await sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest
            {
                EventoId = eventId,
                TicketTypeId = ticketTypeId,
                Cantidad = 2
            });

            Assert.Equal(2, tickets.Count);
            Assert.All(tickets, t => Assert.Equal(clientePersonaId, t.ClientePersonaId));

            foreach (var ticket in tickets)
            {
                var snapshot = await _fixture.Db!.Collection("tickets").Document(ticket.Id).GetSnapshotAsync();
                var persisted = snapshot.ConvertTo<Ticket>();
                Assert.Equal(clientePersonaId, persisted.ClientePersonaId);
                Assert.NotEqual(clienteUid, persisted.ClientePersonaId);
            }
        }

        [FirestoreEmulatorFact]
        public async Task GetTicketsByClienteIdAsync_DoesNotReturnOtherPersonasTickets()
        {
            var uidA = $"uid-a-{Guid.NewGuid():N}";
            var personaAId = $"persona-a-{Guid.NewGuid():N}";
            var personaBId = $"persona-b-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((uidA, personaAId));

            var sut = new TicketService(
                _fixture.Db!,
                personaResolver.Object,
                Mock.Of<IControlAsignacionRepository>(),
                Mock.Of<ITicketValidationStore>(),
                Mock.Of<ILogger<TicketService>>());

            var ticketA = $"ticket-a-{Guid.NewGuid():N}";
            var ticketB = $"ticket-b-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("tickets").Document(ticketA).SetAsync(new Ticket { Id = ticketA, ClientePersonaId = personaAId, EventoId = "event-x" });
            await _fixture.Db!.Collection("tickets").Document(ticketB).SetAsync(new Ticket { Id = ticketB, ClientePersonaId = personaBId, EventoId = "event-x" });

            var propios = (await sut.GetTicketsByClienteIdAsync(uidA)).ToList();

            Assert.Single(propios);
            Assert.Equal(ticketA, propios[0].Id);
            Assert.Equal(personaAId, propios[0].ClientePersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task ValidateTicketAsync_ControlAssigned_PersistsValidadoPorPersonaId_AndMarksUsado()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((controlUid, controlPersonaId));

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);

            var sut = new TicketService(
                _fixture.Db!,
                personaResolver.Object,
                controlAsignacionRepository,
                validationStore,
                Mock.Of<ILogger<TicketService>>());

            var eventId = $"event-{Guid.NewGuid():N}";
            var ticketId = $"ticket-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
            {
                Id = ticketId,
                EventoId = eventId,
                ClientePersonaId = "cliente-persona-1",
                Estado = Ticket.TicketStatus.Emitido,
            });

            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1");

            var outcome = await sut.ValidateTicketAsync(controlUid, ticketId, eventId);
            Assert.Equal(TicketValidationOutcome.Success, outcome);

            var snapshot = await _fixture.Db!.Collection("tickets").Document(ticketId).GetSnapshotAsync();
            var persisted = snapshot.ConvertTo<Ticket>();
            Assert.Equal(Ticket.TicketStatus.Usado, persisted.Estado);
            Assert.Equal(controlPersonaId, persisted.ValidadoPorPersonaId);

            // Segundo uso del mismo ticket sigue rechazado.
            var second = await sut.ValidateTicketAsync(controlUid, ticketId, eventId);
            Assert.Equal(TicketValidationOutcome.AlreadyUsed, second);
        }

        [FirestoreEmulatorFact]
        public async Task ValidateTicketAsync_ControlNotAssignedToEvent_ReturnsNotAuthorized()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((controlUid, controlPersonaId));

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);

            var sut = new TicketService(
                _fixture.Db!,
                personaResolver.Object,
                controlAsignacionRepository,
                validationStore,
                Mock.Of<ILogger<TicketService>>());

            var eventId = $"event-{Guid.NewGuid():N}";
            var ticketId = $"ticket-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
            {
                Id = ticketId,
                EventoId = eventId,
                ClientePersonaId = "cliente-persona-1",
                Estado = Ticket.TicketStatus.Emitido,
            });

            // Nunca se llamó AsignarAsync para este Control+Evento.
            var outcome = await sut.ValidateTicketAsync(controlUid, ticketId, eventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }
    }
}
