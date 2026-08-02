using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
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

        // Cloud Firestore (y el Emulator) persiste Timestamp con precisión de microsegundo, más
        // gruesa que los 100ns de un DateTime.Ticks: sin truncar, comparar un DateTime.UtcNow
        // "de fábrica" contra el valor releído tras el round-trip falla por unos pocos ticks.
        // Se trunca al construir la fecha esperada para que ambos lados sean exactamente iguales.
        private static DateTime TruncateToMicroseconds(DateTime value) =>
            new DateTime(value.Ticks - (value.Ticks % 10), value.Kind);

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
            await _fixture.Db!.Collection("tickets").Document(ticketA).SetAsync(new Ticket { Id = ticketA, ClientePersonaId = personaAId, EventoId = "event-x", FechaInicio = DateTime.UtcNow.AddDays(-1), FechaFin = DateTime.UtcNow.AddDays(1) });
            await _fixture.Db!.Collection("tickets").Document(ticketB).SetAsync(new Ticket { Id = ticketB, ClientePersonaId = personaBId, EventoId = "event-x", FechaInicio = DateTime.UtcNow.AddDays(-1), FechaFin = DateTime.UtcNow.AddDays(1) });

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
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento validable",
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddDays(1),
                Estado = Event.EventStatus.Publicado
            });
            await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
            {
                Id = ticketId,
                EventoId = eventId,
                ClientePersonaId = "cliente-persona-1",
                Estado = Ticket.TicketStatus.Emitido,
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddDays(1),
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
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddDays(1),
            });

            // Nunca se llamó AsignarAsync para este Control+Evento.
            var outcome = await sut.ValidateTicketAsync(controlUid, ticketId, eventId);

            Assert.Equal(TicketValidationOutcome.NotAuthorized, outcome);
        }

        // ---- BuyTicketsAsync: vigencia de compra (docs/api-mvp-plan.md §0.1/§3) ----

        private static TicketService BuildTicketService(
            FirestoreEmulatorFixture fixture,
            Mock<IAuthenticatedPersonaResolver> personaResolver,
            IControlAsignacionRepository? controlAsignacionRepository = null,
            ITicketValidationStore? validationStore = null) =>
            new TicketService(
                fixture.Db!,
                personaResolver.Object,
                controlAsignacionRepository ?? Mock.Of<IControlAsignacionRepository>(),
                validationStore ?? Mock.Of<ITicketValidationStore>(),
                Mock.Of<ILogger<TicketService>>());

        private async Task<(string eventId, string ticketTypeId)> SeedEventoAsync(
            Event.EventStatus estado,
            DateTime fechaInicio,
            DateTime fechaFin,
            int cantidadDisponible = 10,
            decimal precio = 100)
        {
            var eventId = $"event-{Guid.NewGuid():N}";
            var ticketTypeId = $"tipo-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento de prueba",
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                Estado = estado,
                TicketTypes = new List<TicketType>
                {
                    new TicketType { Id = ticketTypeId, Nombre = "General", Precio = precio, CantidadDisponible = cantidadDisponible, EventoId = eventId }
                }
            });
            return (eventId, ticketTypeId);
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_PublicadoAntesDeInicio_Succeeds_WithServerSidePhoto()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var clientePersonaId = $"persona-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, clientePersonaId));
            var sut = BuildTicketService(_fixture, personaResolver);

            var fechaInicio = TruncateToMicroseconds(DateTime.UtcNow.AddDays(5));
            var fechaFin = TruncateToMicroseconds(DateTime.UtcNow.AddDays(6));
            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Publicado,
                fechaInicio: fechaInicio,
                fechaFin: fechaFin,
                precio: 250);

            var tickets = await sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest
            {
                EventoId = eventId,
                TicketTypeId = ticketTypeId,
                Cantidad = 1
            });

            var ticket = Assert.Single(tickets);
            Assert.Equal("Evento de prueba", ticket.EventoNombre);
            Assert.Equal("General", ticket.TicketTypeNombre);
            Assert.Equal(250, ticket.PrecioPagado);
            // Ambas se copian exactamente del Event leído dentro de la transacción de compra
            // (docs/api-mvp-plan.md §3), con precisión de tick — nunca del request del cliente
            // (TicketBuyRequest no tiene siquiera un campo de fecha).
            Assert.Equal(fechaInicio, ticket.FechaInicio);
            Assert.Equal(fechaFin, ticket.FechaFin);
            Assert.Equal("Emitido", ticket.Estado);
            Assert.True(ticket.Utilizable);
            Assert.Null(ticket.MotivoNoUtilizable);

            var persisted = (await _fixture.Db!.Collection("tickets").Document(ticket.Id).GetSnapshotAsync()).ConvertTo<Ticket>();
            Assert.Equal("Evento de prueba", persisted.EventoNombre);
            Assert.Equal("General", persisted.TicketTypeNombre);
            Assert.Equal(250, persisted.PrecioPagado);
            Assert.Equal(fechaInicio, persisted.FechaInicio);
            Assert.Equal(fechaFin, persisted.FechaFin);
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_EventoBorrador_ThrowsEventoNoDisponibleParaCompra()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, $"persona-{Guid.NewGuid():N}"));
            var sut = BuildTicketService(_fixture, personaResolver);

            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Borrador,
                fechaInicio: DateTime.UtcNow.AddDays(5),
                fechaFin: DateTime.UtcNow.AddDays(6));

            await Assert.ThrowsAsync<EventoNoDisponibleParaCompraException>(() =>
                sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 }));
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_EventoCancelado_ThrowsEventoNoDisponibleParaCompra()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, $"persona-{Guid.NewGuid():N}"));
            var sut = BuildTicketService(_fixture, personaResolver);

            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Cancelado,
                fechaInicio: DateTime.UtcNow.AddDays(5),
                fechaFin: DateTime.UtcNow.AddDays(6));

            await Assert.ThrowsAsync<EventoNoDisponibleParaCompraException>(() =>
                sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 }));
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_EventoPublicadoYaIniciado_ThrowsEventoNoDisponibleParaCompra()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, $"persona-{Guid.NewGuid():N}"));
            var sut = BuildTicketService(_fixture, personaResolver);

            // Publicado y en curso (FechaInicio ya pasó, FechaFin todavía no): sigue visible y
            // validable, pero ya no admite compra (docs/api-mvp-plan.md §0.1).
            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddHours(-1),
                fechaFin: DateTime.UtcNow.AddHours(1));

            await Assert.ThrowsAsync<EventoNoDisponibleParaCompraException>(() =>
                sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 }));
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_EventoFinalizado_ThrowsEventoNoDisponibleParaCompra()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, $"persona-{Guid.NewGuid():N}"));
            var sut = BuildTicketService(_fixture, personaResolver);

            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-3),
                fechaFin: DateTime.UtcNow.AddDays(-1));

            await Assert.ThrowsAsync<EventoNoDisponibleParaCompraException>(() =>
                sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 }));
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_EventoInexistente_ThrowsEventNotFound()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, $"persona-{Guid.NewGuid():N}"));
            var sut = BuildTicketService(_fixture, personaResolver);

            await Assert.ThrowsAsync<EventNotFoundException>(() =>
                sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest
                {
                    EventoId = $"evento-inexistente-{Guid.NewGuid():N}",
                    TicketTypeId = "cualquiera",
                    Cantidad = 1
                }));
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_TicketTypeInvalido_ThrowsTicketTypeInvalido()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, $"persona-{Guid.NewGuid():N}"));
            var sut = BuildTicketService(_fixture, personaResolver);

            var (eventId, _) = await SeedEventoAsync(
                Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(5),
                fechaFin: DateTime.UtcNow.AddDays(6));

            await Assert.ThrowsAsync<TicketTypeInvalidoException>(() =>
                sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest
                {
                    EventoId = eventId,
                    TicketTypeId = $"tipo-inexistente-{Guid.NewGuid():N}",
                    Cantidad = 1
                }));
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_StockInsuficiente_ThrowsStockInsuficiente()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, $"persona-{Guid.NewGuid():N}"));
            var sut = BuildTicketService(_fixture, personaResolver);

            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(5),
                fechaFin: DateTime.UtcNow.AddDays(6),
                cantidadDisponible: 1);

            await Assert.ThrowsAsync<StockInsuficienteException>(() =>
                sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 2 }));
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_ConcurrentPurchases_StockOne_OnlyOneSucceeds()
        {
            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(5),
                fechaFin: DateTime.UtcNow.AddDays(6),
                cantidadDisponible: 1);

            const int concurrentBuyers = 8;
            var tasks = new List<Task<bool>>();

            for (int i = 0; i < concurrentBuyers; i++)
            {
                var uid = $"uid-{i}-{Guid.NewGuid():N}";
                var personaResolver = ResolverFor((uid, $"persona-{i}-{Guid.NewGuid():N}"));
                var sut = BuildTicketService(_fixture, personaResolver);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await sut.BuyTicketsAsync(uid, new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 });
                        return true;
                    }
                    catch (StockInsuficienteException)
                    {
                        return false;
                    }
                }));
            }

            var results = await Task.WhenAll(tasks);

            Assert.Single(results, r => r);
            Assert.Equal(concurrentBuyers - 1, results.Count(r => !r));

            var ticketsSnapshot = await _fixture.Db!.Collection("tickets")
                .WhereEqualTo(nameof(Ticket.EventoId), eventId)
                .GetSnapshotAsync();
            Assert.Single(ticketsSnapshot.Documents);

            var eventoPersistido = (await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync()).ConvertTo<Event>();
            var tipoPersistido = eventoPersistido.TicketTypes.Single(t => t.Id == ticketTypeId);
            Assert.Equal(0, tipoPersistido.CantidadDisponible);
        }

        // ---- ValidateTicketAsync: vigencia de validación (docs/api-mvp-plan.md §0.1/§3) ----

        [FirestoreEmulatorFact]
        public async Task ValidateTicketAsync_EventoCancelado_ReturnsEventoCancelado_AndDoesNotTouchTicketEstado()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((controlUid, controlPersonaId));
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var sut = BuildTicketService(_fixture, personaResolver, controlAsignacionRepository, validationStore);

            var eventId = $"event-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento cancelado",
                FechaInicio = DateTime.UtcNow.AddDays(-2),
                FechaFin = DateTime.UtcNow.AddDays(2),
                Estado = Event.EventStatus.Cancelado
            });

            var ticketId = $"ticket-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
            {
                Id = ticketId,
                EventoId = eventId,
                ClientePersonaId = "cliente-persona-1",
                Estado = Ticket.TicketStatus.Emitido,
                FechaInicio = DateTime.UtcNow.AddDays(-2),
                FechaFin = DateTime.UtcNow.AddDays(2),
            });

            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1");

            var outcome = await sut.ValidateTicketAsync(controlUid, ticketId, eventId);
            Assert.Equal(TicketValidationOutcome.EventoCancelado, outcome);

            var persisted = (await _fixture.Db!.Collection("tickets").Document(ticketId).GetSnapshotAsync()).ConvertTo<Ticket>();
            Assert.Equal(Ticket.TicketStatus.Emitido, persisted.Estado);
        }

        [FirestoreEmulatorFact]
        public async Task ValidateTicketAsync_EventoFinalizado_ReturnsEventoFinalizado()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((controlUid, controlPersonaId));
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var sut = BuildTicketService(_fixture, personaResolver, controlAsignacionRepository, validationStore);

            var eventId = $"event-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento finalizado",
                FechaInicio = DateTime.UtcNow.AddDays(-3),
                FechaFin = DateTime.UtcNow.AddDays(-1),
                Estado = Event.EventStatus.Publicado
            });

            var ticketId = $"ticket-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
            {
                Id = ticketId,
                EventoId = eventId,
                ClientePersonaId = "cliente-persona-1",
                Estado = Ticket.TicketStatus.Emitido,
                FechaInicio = DateTime.UtcNow.AddDays(-3),
                FechaFin = DateTime.UtcNow.AddDays(-1),
            });

            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1");

            var outcome = await sut.ValidateTicketAsync(controlUid, ticketId, eventId);
            Assert.Equal(TicketValidationOutcome.EventoFinalizado, outcome);
        }

        [FirestoreEmulatorFact]
        public async Task ValidateTicketAsync_EventoPublicadoEnCurso_ReturnsSuccess()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((controlUid, controlPersonaId));
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var sut = BuildTicketService(_fixture, personaResolver, controlAsignacionRepository, validationStore);

            // En curso: FechaInicio ya pasó, FechaFin todavía no. La validación no depende de
            // FechaInicio (docs/api-mvp-plan.md §0.1): debe aceptarse igual.
            var eventId = $"event-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento en curso",
                FechaInicio = DateTime.UtcNow.AddHours(-1),
                FechaFin = DateTime.UtcNow.AddHours(1),
                Estado = Event.EventStatus.Publicado
            });

            var ticketId = $"ticket-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
            {
                Id = ticketId,
                EventoId = eventId,
                ClientePersonaId = "cliente-persona-1",
                Estado = Ticket.TicketStatus.Emitido,
                FechaInicio = DateTime.UtcNow.AddHours(-1),
                FechaFin = DateTime.UtcNow.AddHours(1),
            });

            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1");

            var outcome = await sut.ValidateTicketAsync(controlUid, ticketId, eventId);
            Assert.Equal(TicketValidationOutcome.Success, outcome);
        }

        [FirestoreEmulatorFact]
        public async Task ValidateTicketAsync_ConcurrentValidations_OnlyOneSucceeds()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((controlUid, controlPersonaId));
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var sut = BuildTicketService(_fixture, personaResolver, controlAsignacionRepository, validationStore);

            var eventId = $"event-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento concurrente",
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddDays(1),
                Estado = Event.EventStatus.Publicado
            });

            var ticketId = $"ticket-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
            {
                Id = ticketId,
                EventoId = eventId,
                ClientePersonaId = "cliente-persona-1",
                Estado = Ticket.TicketStatus.Emitido,
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddDays(1),
            });

            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1");

            var task1 = Task.Run(() => sut.ValidateTicketAsync(controlUid, ticketId, eventId));
            var task2 = Task.Run(() => sut.ValidateTicketAsync(controlUid, ticketId, eventId));

            var results = await Task.WhenAll(task1, task2);

            Assert.Single(results, r => r == TicketValidationOutcome.Success);
            Assert.Single(results, r => r == TicketValidationOutcome.AlreadyUsed);
        }

        // ---- GetTicketsByClienteIdAsync: contrato de /api/tickets/me (docs/api-mvp-plan.md §3) ----

        [FirestoreEmulatorFact]
        public async Task GetTicketsByClienteIdAsync_ReflectsUtilizable_ForEachMotivo()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var clientePersonaId = $"persona-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, clientePersonaId));
            var sut = BuildTicketService(_fixture, personaResolver);

            var eventoUsado = $"event-usado-{Guid.NewGuid():N}";
            var eventoAnulado = $"event-anulado-{Guid.NewGuid():N}";
            var eventoCancelado = $"event-cancelado-{Guid.NewGuid():N}";
            var eventoFinalizado = $"event-finalizado-{Guid.NewGuid():N}";
            var eventoVigente = $"event-vigente-{Guid.NewGuid():N}";

            async Task SeedEvent(string id, Event.EventStatus estado, DateTime inicio, DateTime fin) =>
                await _fixture.Db!.Collection("events").Document(id).SetAsync(new Event
                {
                    Id = id,
                    Nombre = "Evento " + id,
                    FechaInicio = inicio,
                    FechaFin = fin,
                    Estado = estado
                });

            await SeedEvent(eventoUsado, Event.EventStatus.Publicado, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
            await SeedEvent(eventoAnulado, Event.EventStatus.Publicado, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
            await SeedEvent(eventoCancelado, Event.EventStatus.Cancelado, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(2));
            await SeedEvent(eventoFinalizado, Event.EventStatus.Publicado, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(-1));
            await SeedEvent(eventoVigente, Event.EventStatus.Publicado, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

            // Fotografía deliberadamente distinta de las fechas actuales de cada Event
            // (docs/api-mvp-plan.md §3): prueba que TicketResponseDto.FechaInicio/FechaFin salen
            // de esta fotografía persistida, nunca del Event vigente (que para eventoCancelado y
            // eventoFinalizado tiene fechas distintas a propósito).
            var fotoInicio = TruncateToMicroseconds(DateTime.UtcNow.AddDays(-10));
            var fotoFin = TruncateToMicroseconds(DateTime.UtcNow.AddDays(-9));

            async Task<string> SeedTicket(string eventId, Ticket.TicketStatus estado)
            {
                var ticketId = $"ticket-{Guid.NewGuid():N}";
                await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
                {
                    Id = ticketId,
                    EventoId = eventId,
                    ClientePersonaId = clientePersonaId,
                    Estado = estado,
                    EventoNombre = "Evento " + eventId,
                    TicketTypeNombre = "General",
                    PrecioPagado = 100,
                    FechaInicio = fotoInicio,
                    FechaFin = fotoFin,
                });
                return ticketId;
            }

            var ticketUsadoId = await SeedTicket(eventoUsado, Ticket.TicketStatus.Usado);
            var ticketAnuladoId = await SeedTicket(eventoAnulado, Ticket.TicketStatus.Anulado);
            var ticketCanceladoId = await SeedTicket(eventoCancelado, Ticket.TicketStatus.Emitido);
            var ticketFinalizadoId = await SeedTicket(eventoFinalizado, Ticket.TicketStatus.Emitido);
            var ticketVigenteId = await SeedTicket(eventoVigente, Ticket.TicketStatus.Emitido);

            var misTickets = (await sut.GetTicketsByClienteIdAsync(clienteUid)).ToDictionary(t => t.Id);

            Assert.False(misTickets[ticketUsadoId].Utilizable);
            Assert.Equal("Usado", misTickets[ticketUsadoId].MotivoNoUtilizable);
            Assert.Equal("Usado", misTickets[ticketUsadoId].Estado);

            Assert.False(misTickets[ticketAnuladoId].Utilizable);
            Assert.Equal("Anulado", misTickets[ticketAnuladoId].MotivoNoUtilizable);

            Assert.False(misTickets[ticketCanceladoId].Utilizable);
            Assert.Equal("EventoCancelado", misTickets[ticketCanceladoId].MotivoNoUtilizable);
            Assert.Equal("Emitido", misTickets[ticketCanceladoId].Estado);

            Assert.False(misTickets[ticketFinalizadoId].Utilizable);
            Assert.Equal("EventoFinalizado", misTickets[ticketFinalizadoId].MotivoNoUtilizable);

            Assert.True(misTickets[ticketVigenteId].Utilizable);
            Assert.Null(misTickets[ticketVigenteId].MotivoNoUtilizable);

            Assert.All(misTickets.Values, t => Assert.Equal("General", t.TicketTypeNombre));
            Assert.All(misTickets.Values, t => Assert.Equal(100, t.PrecioPagado));

            // FechaInicio/FechaFin siempre salen de la fotografía del Ticket, incluso para los
            // eventos cuyo FechaInicio/FechaFin actual (seedeado arriba) es distinto.
            Assert.All(misTickets.Values, t => Assert.Equal(fotoInicio, t.FechaInicio));
            Assert.All(misTickets.Values, t => Assert.Equal(fotoFin, t.FechaFin));
        }

        [FirestoreEmulatorFact]
        public async Task CancelarEvento_CambiaUtilizable_PeroNoLaFotografiaHistoricaDelTicket()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var clientePersonaId = $"persona-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, clientePersonaId));
            var sut = BuildTicketService(_fixture, personaResolver);

            var fechaInicio = TruncateToMicroseconds(DateTime.UtcNow.AddDays(5));
            var fechaFin = TruncateToMicroseconds(DateTime.UtcNow.AddDays(6));
            var (eventId, ticketTypeId) = await SeedEventoAsync(
                Event.EventStatus.Publicado,
                fechaInicio: fechaInicio,
                fechaFin: fechaFin,
                precio: 300);

            var comprados = await sut.BuyTicketsAsync(clienteUid, new TicketBuyRequest
            {
                EventoId = eventId,
                TicketTypeId = ticketTypeId,
                Cantidad = 1
            });
            var comprado = Assert.Single(comprados);
            Assert.True(comprado.Utilizable);

            // Cancelación directa del documento del Event: sólo interesa aquí que la lectura de
            // tickets refleje el estado actual sin reescribir el historial del Ticket
            // (docs/api-mvp-plan.md §3: cancelar nunca hace un batch-update de tickets).
            await _fixture.Db!.Collection("events").Document(eventId).UpdateAsync("Estado", Event.EventStatus.Cancelado);

            var misTickets = await sut.GetTicketsByClienteIdAsync(clienteUid);
            var actualizado = Assert.Single(misTickets, t => t.Id == comprado.Id);

            Assert.False(actualizado.Utilizable);
            Assert.Equal("EventoCancelado", actualizado.MotivoNoUtilizable);

            // La fotografía histórica no cambia por la cancelación.
            Assert.Equal("Evento de prueba", actualizado.EventoNombre);
            Assert.Equal("General", actualizado.TicketTypeNombre);
            Assert.Equal(300, actualizado.PrecioPagado);
            Assert.Equal(fechaInicio, actualizado.FechaInicio);
            Assert.Equal(fechaFin, actualizado.FechaFin);
            Assert.Equal("Emitido", actualizado.Estado);

            var persistedTicket = (await _fixture.Db!.Collection("tickets").Document(comprado.Id).GetSnapshotAsync()).ConvertTo<Ticket>();
            Assert.Equal(fechaInicio, persistedTicket.FechaInicio);
            Assert.Equal(fechaFin, persistedTicket.FechaFin);
            Assert.Equal(Ticket.TicketStatus.Emitido, persistedTicket.Estado);
        }

        [FirestoreEmulatorFact]
        public async Task GetTicketsByClienteIdAsync_MultipleTicketsSameEvent_ResolveConsistently()
        {
            var clienteUid = $"uid-{Guid.NewGuid():N}";
            var clientePersonaId = $"persona-{Guid.NewGuid():N}";
            var personaResolver = ResolverFor((clienteUid, clientePersonaId));
            var sut = BuildTicketService(_fixture, personaResolver);

            var eventId = $"event-compartido-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento compartido",
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddDays(1),
                Estado = Event.EventStatus.Publicado
            });

            async Task<string> SeedTicket()
            {
                var ticketId = $"ticket-{Guid.NewGuid():N}";
                await _fixture.Db!.Collection("tickets").Document(ticketId).SetAsync(new Ticket
                {
                    Id = ticketId,
                    EventoId = eventId,
                    ClientePersonaId = clientePersonaId,
                    Estado = Ticket.TicketStatus.Emitido,
                    EventoNombre = "Evento compartido",
                    TicketTypeNombre = "General",
                    PrecioPagado = 100,
                    FechaInicio = DateTime.UtcNow.AddDays(-1),
                    FechaFin = DateTime.UtcNow.AddDays(1),
                });
                return ticketId;
            }

            // Tres tickets del mismo Event: GetTicketsByClienteIdAsync debe agruparlos por
            // EventoId y resolver el Event actual una única vez, no una lectura por ticket
            // (docs/api-mvp-plan.md §3).
            var ticket1Id = await SeedTicket();
            var ticket2Id = await SeedTicket();
            var ticket3Id = await SeedTicket();

            // El evento se cancela después de emitidos los tres tickets.
            await _fixture.Db!.Collection("events").Document(eventId).UpdateAsync("Estado", Event.EventStatus.Cancelado);

            var misTickets = (await sut.GetTicketsByClienteIdAsync(clienteUid)).ToDictionary(t => t.Id);

            // Los tres reflejan exactamente el mismo resultado derivado de una única resolución
            // agrupada del Event, no lecturas independientes potencialmente inconsistentes.
            foreach (var ticketId in new[] { ticket1Id, ticket2Id, ticket3Id })
            {
                Assert.False(misTickets[ticketId].Utilizable);
                Assert.Equal("EventoCancelado", misTickets[ticketId].MotivoNoUtilizable);
                Assert.Equal("Emitido", misTickets[ticketId].Estado);
            }
        }
    }
}
