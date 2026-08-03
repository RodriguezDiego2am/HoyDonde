using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // docs/api-mvp-plan.md §1/§2 (API-MVP 1 — Evento) ejercitado contra Firestore Emulator real:
    // máquina de estados, vigencia FechaInicio/FechaFin, validaciones de datos y visibilidad
    // pública/propia. El actor (UID de Firebase) se resuelve a PersonaId vía un
    // IAuthenticatedPersonaResolver mockeado (esa resolución en sí ya está cubierta por
    // AuthenticatedPersonaResolverTests) para poder fijar deterministicamente qué PersonaId
    // corresponde a cada UID de prueba.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class EventServiceEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public EventServiceEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static EventService CreateSut(FirestoreEmulatorFixture fixture, params (string uid, string personaId)[] actors)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            foreach (var (uid, personaId) in actors)
            {
                personaResolver.Setup(r => r.ResolvePersonaIdAsync(uid)).ReturnsAsync(personaId);
            }
            var logger = new Mock<ILogger<EventService>>();
            return new EventService(fixture.Db!, personaResolver.Object, logger.Object);
        }

        private static Event BuildEvent(
            string id,
            string organizadorPersonaId,
            Event.EventStatus estado = Event.EventStatus.Borrador,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null,
            List<TicketType>? ticketTypes = null,
            Event.EventCategory categoria = Event.EventCategory.Musica,
            string? ubicacion = null)
        {
            return new Event
            {
                Id = id,
                Nombre = "Evento de prueba",
                Descripcion = "Descripcion",
                Ubicacion = ubicacion ?? "Buenos Aires",
                Categoria = categoria,
                FechaInicio = fechaInicio ?? DateTime.UtcNow.AddDays(5),
                FechaFin = fechaFin ?? DateTime.UtcNow.AddDays(6),
                OrganizadorPersonaId = organizadorPersonaId,
                Estado = estado,
                TicketTypes = ticketTypes ?? new List<TicketType>
                {
                    new TicketType { Id = Guid.NewGuid().ToString(), Nombre = "General", Precio = 100, CantidadDisponible = 10 }
                }
            };
        }

        private async Task SeedEventAsync(Event evento)
        {
            await _fixture.Db!.Collection("events").Document(evento.Id).SetAsync(evento);
        }

        private static EventCreateRequest ValidCreateRequest() => new EventCreateRequest
        {
            Nombre = "Evento de prueba",
            Descripcion = "Descripcion",
            Ubicacion = "Buenos Aires",
            Categoria = Event.EventCategory.Musica,
            FechaInicio = DateTime.UtcNow.AddDays(5),
            FechaFin = DateTime.UtcNow.AddDays(6),
            TicketGroups = new List<TicketGroupDto>
            {
                new TicketGroupDto { Nombre = "General", Precio = 100, CantidadDisponible = 10 }
            }
        };

        // ---- CreateEventAsync: happy path + validaciones ----

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_PersistsOrganizadorPersonaId_NotUid()
        {
            var organizadorUid = $"uid-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (organizadorUid, organizadorPersonaId));

            var response = await sut.CreateEventAsync(ValidCreateRequest(), organizadorUid);

            var snapshot = await _fixture.Db!.Collection("events").Document(response.Id).GetSnapshotAsync();
            Assert.True(snapshot.Exists);
            var evento = snapshot.ConvertTo<Event>();

            Assert.Equal(organizadorPersonaId, evento.OrganizadorPersonaId);
            Assert.NotEqual(organizadorUid, evento.OrganizadorPersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_ValidRequest_StartsInBorradorAndPersistsFechas()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var request = ValidCreateRequest();
            var response = await sut.CreateEventAsync(request, uid);

            Assert.Equal(Event.EventEffectiveStatus.Borrador, response.Estado);
            Assert.Equal(request.FechaInicio, response.FechaInicio);
            Assert.Equal(request.FechaFin, response.FechaFin);
            Assert.Single(response.TicketGroups);

            var snapshot = await _fixture.Db!.Collection("events").Document(response.Id).GetSnapshotAsync();
            var evento = snapshot.ConvertTo<Event>();
            Assert.Equal(Event.EventStatus.Borrador, evento.Estado);
        }

        // Contrato de salida de tipos de ticket: EventResponse.TicketGroups usa
        // TicketTypeResponseDto (con el TicketTypeId real generado por el servidor), nunca el DTO
        // de entrada TicketGroupDto ni el modelo de persistencia TicketType.
        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_TicketTypeResponse_HasServerGeneratedNonEmptyId_MatchingPersistedTicketType()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var response = await sut.CreateEventAsync(ValidCreateRequest(), uid);

            var ticketTypeResponse = Assert.Single(response.TicketGroups);
            Assert.False(string.IsNullOrEmpty(ticketTypeResponse.Id));
            Assert.Equal("General", ticketTypeResponse.Nombre);
            Assert.Equal(100, ticketTypeResponse.Precio);
            Assert.Equal(10, ticketTypeResponse.CantidadDisponible);

            var snapshot = await _fixture.Db!.Collection("events").Document(response.Id).GetSnapshotAsync();
            var persistedTicketType = snapshot.ConvertTo<Event>().TicketTypes.Single();
            Assert.Equal(persistedTicketType.Id, ticketTypeResponse.Id);
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_EmptyNombre_ThrowsEventValidationException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.Nombre = "   ";

            await Assert.ThrowsAsync<EventValidationException>(() => sut.CreateEventAsync(request, uid));
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_EmptyUbicacion_ThrowsEventValidationException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.Ubicacion = "";

            await Assert.ThrowsAsync<EventValidationException>(() => sut.CreateEventAsync(request, uid));
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_NoTicketGroups_ThrowsEventValidationException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.TicketGroups = new List<TicketGroupDto>();

            await Assert.ThrowsAsync<EventValidationException>(() => sut.CreateEventAsync(request, uid));
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_NegativePrecio_ThrowsEventValidationException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.TicketGroups = new List<TicketGroupDto>
            {
                new TicketGroupDto { Nombre = "General", Precio = -1, CantidadDisponible = 10 }
            };

            await Assert.ThrowsAsync<EventValidationException>(() => sut.CreateEventAsync(request, uid));
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_CantidadMenorQueUno_ThrowsEventValidationException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.TicketGroups = new List<TicketGroupDto>
            {
                new TicketGroupDto { Nombre = "General", Precio = 10, CantidadDisponible = 0 }
            };

            await Assert.ThrowsAsync<EventValidationException>(() => sut.CreateEventAsync(request, uid));
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_FechaInicioPasada_ThrowsEventValidationException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.FechaInicio = DateTime.UtcNow.AddDays(-1);

            await Assert.ThrowsAsync<EventValidationException>(() => sut.CreateEventAsync(request, uid));
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_FechaFinAntesDeFechaInicio_ThrowsEventValidationException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.FechaInicio = DateTime.UtcNow.AddDays(5);
            request.FechaFin = DateTime.UtcNow.AddDays(4);

            await Assert.ThrowsAsync<EventValidationException>(() => sut.CreateEventAsync(request, uid));
        }

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_PrecioCero_Succeeds()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var request = ValidCreateRequest();
            request.TicketGroups = new List<TicketGroupDto>
            {
                new TicketGroupDto { Nombre = "Gratis", Precio = 0, CantidadDisponible = 5 }
            };

            var response = await sut.CreateEventAsync(request, uid);

            Assert.Equal(0, response.TicketGroups.Single().Precio);
        }

        // ---- PublishEventAsync: ownership, transición, condiciones ----

        [FirestoreEmulatorFact]
        public async Task PublishEventAsync_ForForeignEvent_ThrowsEventOwnershipException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var extranioUid = $"uid-extranio-{Guid.NewGuid():N}";
            var extranioPersonaId = $"persona-extranio-{Guid.NewGuid():N}";

            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId), (extranioUid, extranioPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId));

            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.PublishEventAsync(eventId, extranioUid));
            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.CancelEventAsync(eventId, extranioUid));
            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.UpdateEventAsync(eventId, extranioUid, new EventUpdateRequest
            {
                Nombre = "Intento ajeno",
                Ubicacion = "Otro lugar",
                FechaInicio = DateTime.UtcNow.AddDays(1),
                FechaFin = DateTime.UtcNow.AddDays(2),
                TicketGroups = new List<TicketGroupDto>
                {
                    new TicketGroupDto { Nombre = "General", Precio = 10, CantidadDisponible = 1 }
                }
            }));
        }

        [FirestoreEmulatorFact]
        public async Task PublishEventAsync_OwnEvent_Succeeds()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId));

            await sut.PublishEventAsync(eventId, duenoUid);

            var snapshot = await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync();
            Assert.Equal(Event.EventStatus.Publicado, snapshot.ConvertTo<Event>().Estado);
        }

        [FirestoreEmulatorFact]
        public async Task PublishEventAsync_WithoutTicketTypes_ThrowsEventMissingTicketTypesException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, ticketTypes: new List<TicketType>()));

            await Assert.ThrowsAsync<EventMissingTicketTypesException>(() => sut.PublishEventAsync(eventId, duenoUid));
        }

        [FirestoreEmulatorFact]
        public async Task PublishEventAsync_AlreadyPublicado_ThrowsEventInvalidTransitionException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Publicado));

            await Assert.ThrowsAsync<EventInvalidTransitionException>(() => sut.PublishEventAsync(eventId, duenoUid));
        }

        [FirestoreEmulatorFact]
        public async Task PublishEventAsync_Cancelado_ThrowsEventInvalidTransitionException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Cancelado));

            await Assert.ThrowsAsync<EventInvalidTransitionException>(() => sut.PublishEventAsync(eventId, duenoUid));
        }

        // ---- CancelEventAsync: transición según §1, evaluando estado efectivo ----

        [FirestoreEmulatorFact]
        public async Task CancelEventAsync_FromBorrador_Succeeds()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Borrador));

            await sut.CancelEventAsync(eventId, duenoUid);

            var snapshot = await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync();
            Assert.Equal(Event.EventStatus.Cancelado, snapshot.ConvertTo<Event>().Estado);
        }

        [FirestoreEmulatorFact]
        public async Task CancelEventAsync_FromPublicadoEnCurso_Succeeds()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            // Evento en curso: ya empezó (FechaInicio en el pasado) pero todavía no terminó.
            await SeedEventAsync(BuildEvent(
                eventId, duenoPersonaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddHours(-1),
                fechaFin: DateTime.UtcNow.AddHours(1)));

            await sut.CancelEventAsync(eventId, duenoUid);

            var snapshot = await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync();
            Assert.Equal(Event.EventStatus.Cancelado, snapshot.ConvertTo<Event>().Estado);
        }

        [FirestoreEmulatorFact]
        public async Task CancelEventAsync_AlreadyCancelado_ThrowsEventInvalidTransitionException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Cancelado));

            await Assert.ThrowsAsync<EventInvalidTransitionException>(() => sut.CancelEventAsync(eventId, duenoUid));
        }

        [FirestoreEmulatorFact]
        public async Task CancelEventAsync_Finalizado_ThrowsEventInvalidTransitionException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                eventId, duenoPersonaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-2),
                fechaFin: DateTime.UtcNow.AddDays(-1)));

            await Assert.ThrowsAsync<EventInvalidTransitionException>(() => sut.CancelEventAsync(eventId, duenoUid));

            // La cancelación rechazada no debe haber mutado el estado persistido.
            var snapshot = await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync();
            Assert.Equal(Event.EventStatus.Publicado, snapshot.ConvertTo<Event>().Estado);
        }

        // ---- UpdateEventAsync: solo Borrador, reemplazo completo de TicketGroups ----

        [FirestoreEmulatorFact]
        public async Task UpdateEventAsync_OnBorrador_ReplacesTicketGroups_Succeeds()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId));

            var request = new EventUpdateRequest
            {
                Nombre = "Evento actualizado",
                Descripcion = "Nueva descripcion",
                Ubicacion = "Nueva ubicacion",
                Categoria = Event.EventCategory.Tecnologia,
                FechaInicio = DateTime.UtcNow.AddDays(10),
                FechaFin = DateTime.UtcNow.AddDays(11),
                TicketGroups = new List<TicketGroupDto>
                {
                    new TicketGroupDto { Nombre = "VIP", Precio = 500, CantidadDisponible = 5 },
                    new TicketGroupDto { Nombre = "General", Precio = 200, CantidadDisponible = 50 }
                }
            };

            var response = await sut.UpdateEventAsync(eventId, duenoUid, request);

            Assert.Equal("Evento actualizado", response.Nombre);
            Assert.Equal(2, response.TicketGroups.Count);
            Assert.All(response.TicketGroups, t => Assert.False(string.IsNullOrEmpty(t.Id)));
            // Los ids de los tipos de ticket reemplazados son nuevos (reemplazo completo de la
            // colección, docs/api-mvp-plan.md §2), pero siguen siendo generados por el servidor.
            Assert.Equal(2, response.TicketGroups.Select(t => t.Id).Distinct().Count());

            var snapshot = await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync();
            var persisted = snapshot.ConvertTo<Event>();
            Assert.Equal(2, persisted.TicketTypes.Count);
            Assert.Contains(persisted.TicketTypes, t => t.Nombre == "VIP" && t.CantidadDisponible == 5);
            Assert.Contains(persisted.TicketTypes, t => t.Nombre == "General" && t.CantidadDisponible == 50);
            Assert.Equal(persisted.TicketTypes.Select(t => t.Id).OrderBy(x => x).ToList(),
                response.TicketGroups.Select(t => t.Id).OrderBy(x => x).ToList());
        }

        [FirestoreEmulatorFact]
        public async Task UpdateEventAsync_InvalidTicketGroupData_ThrowsEventValidationException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId));

            var request = new EventUpdateRequest
            {
                Nombre = "Evento actualizado",
                Ubicacion = "Nueva ubicacion",
                FechaInicio = DateTime.UtcNow.AddDays(10),
                FechaFin = DateTime.UtcNow.AddDays(11),
                TicketGroups = new List<TicketGroupDto>
                {
                    new TicketGroupDto { Nombre = "VIP", Precio = -50, CantidadDisponible = 5 }
                }
            };

            await Assert.ThrowsAsync<EventValidationException>(() => sut.UpdateEventAsync(eventId, duenoUid, request));
        }

        [FirestoreEmulatorFact]
        public async Task UpdateEventAsync_OnPublicado_ThrowsEventNotEditableException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Publicado));

            await Assert.ThrowsAsync<EventNotEditableException>(() =>
                sut.UpdateEventAsync(eventId, duenoUid, new EventUpdateRequest
                {
                    Nombre = "Intento",
                    Ubicacion = "Lugar",
                    FechaInicio = DateTime.UtcNow.AddDays(1),
                    FechaFin = DateTime.UtcNow.AddDays(2),
                }));
        }

        [FirestoreEmulatorFact]
        public async Task UpdateEventAsync_OnCancelado_ThrowsEventNotEditableException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Cancelado));

            await Assert.ThrowsAsync<EventNotEditableException>(() =>
                sut.UpdateEventAsync(eventId, duenoUid, new EventUpdateRequest
                {
                    Nombre = "Intento",
                    Ubicacion = "Lugar",
                    FechaInicio = DateTime.UtcNow.AddDays(1),
                    FechaFin = DateTime.UtcNow.AddDays(2),
                }));
        }

        [FirestoreEmulatorFact]
        public async Task UpdateEventAsync_OnFinalizado_ThrowsEventNotEditableException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                eventId, duenoPersonaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-2),
                fechaFin: DateTime.UtcNow.AddDays(-1)));

            await Assert.ThrowsAsync<EventNotEditableException>(() =>
                sut.UpdateEventAsync(eventId, duenoUid, new EventUpdateRequest
                {
                    Nombre = "Intento",
                    Ubicacion = "Lugar",
                    FechaInicio = DateTime.UtcNow.AddDays(1),
                    FechaFin = DateTime.UtcNow.AddDays(2),
                }));
        }

        // ---- GetByIdAsync (público): visibilidad §0.1 ----

        [FirestoreEmulatorFact]
        public async Task GetByIdAsync_PublicadoVigente_ReturnsEventResponse()
        {
            var sut = CreateSut(_fixture);
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, "persona-dueno", estado: Event.EventStatus.Publicado));

            var response = await sut.GetByIdAsync(eventId);

            Assert.NotNull(response);
            Assert.Equal(Event.EventEffectiveStatus.Publicado, response!.Estado);
            // Detalle público (GET /api/events/{id}): también expone el TicketTypeId real.
            Assert.False(string.IsNullOrEmpty(Assert.Single(response.TicketGroups).Id));
        }

        [FirestoreEmulatorFact]
        public async Task GetByIdAsync_PublicadoEnCurso_ReturnsEventResponse()
        {
            var sut = CreateSut(_fixture);
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                eventId, "persona-dueno",
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddHours(-1),
                fechaFin: DateTime.UtcNow.AddHours(1)));

            var response = await sut.GetByIdAsync(eventId);

            Assert.NotNull(response);
        }

        [FirestoreEmulatorFact]
        public async Task GetByIdAsync_Borrador_ReturnsNull()
        {
            var sut = CreateSut(_fixture);
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, "persona-dueno", estado: Event.EventStatus.Borrador));

            var response = await sut.GetByIdAsync(eventId);

            Assert.Null(response);
        }

        [FirestoreEmulatorFact]
        public async Task GetByIdAsync_Cancelado_ReturnsNull()
        {
            var sut = CreateSut(_fixture);
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, "persona-dueno", estado: Event.EventStatus.Cancelado));

            var response = await sut.GetByIdAsync(eventId);

            Assert.Null(response);
        }

        [FirestoreEmulatorFact]
        public async Task GetByIdAsync_PublicadoFinalizado_ReturnsNull()
        {
            var sut = CreateSut(_fixture);
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                eventId, "persona-dueno",
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-2),
                fechaFin: DateTime.UtcNow.AddDays(-1)));

            var response = await sut.GetByIdAsync(eventId);

            Assert.Null(response);
        }

        [FirestoreEmulatorFact]
        public async Task GetByIdAsync_Nonexistent_ReturnsNull()
        {
            var sut = CreateSut(_fixture);

            var response = await sut.GetByIdAsync($"event-inexistente-{Guid.NewGuid():N}");

            Assert.Null(response);
        }

        // ---- SearchEventsAsync (catálogo público): misma vigencia que el detalle ----

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_ExcludesNonVisibleStates_IncludesPublicadoVigente()
        {
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";

            var borrador = $"event-borrador-{Guid.NewGuid():N}";
            var cancelado = $"event-cancelado-{Guid.NewGuid():N}";
            var finalizado = $"event-finalizado-{Guid.NewGuid():N}";
            var publicadoVigente = $"event-publicado-{Guid.NewGuid():N}";
            var publicadoEnCurso = $"event-en-curso-{Guid.NewGuid():N}";

            await SeedEventAsync(BuildEvent(borrador, personaId, estado: Event.EventStatus.Borrador));
            await SeedEventAsync(BuildEvent(cancelado, personaId, estado: Event.EventStatus.Cancelado));
            await SeedEventAsync(BuildEvent(
                finalizado, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-2),
                fechaFin: DateTime.UtcNow.AddDays(-1)));
            await SeedEventAsync(BuildEvent(publicadoVigente, personaId, estado: Event.EventStatus.Publicado));
            await SeedEventAsync(BuildEvent(
                publicadoEnCurso, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddHours(-1),
                fechaFin: DateTime.UtcNow.AddHours(1)));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto { Limit = 50 });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.Contains(publicadoVigente, ids);
            Assert.Contains(publicadoEnCurso, ids);
            Assert.DoesNotContain(borrador, ids);
            Assert.DoesNotContain(cancelado, ids);
            Assert.DoesNotContain(finalizado, ids);

            // Búsqueda pública (GET /api/events): cada resultado expone el TicketTypeId real de
            // sus tipos de ticket, nunca una lista vacía de ids ni el modelo Firestore.
            Assert.All(result.Data, e => Assert.All(e.TicketGroups, t => Assert.False(string.IsNullOrEmpty(t.Id))));
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_SoloFechaDesde_CombinesWithFechaFinVisibility_InFirestoreQuery()
        {
            // Ventana de fechas exclusiva de este test (día +200) para no depender de, ni verse
            // afectado por, eventos sembrados por otros tests en la misma colección compartida
            // del emulador.
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var baseFecha = DateTime.UtcNow.AddDays(200);
            var corte = baseFecha.AddDays(3);

            var antesDelCorte = $"event-antes-corte-{Guid.NewGuid():N}";
            var despuesDelCorte = $"event-despues-corte-{Guid.NewGuid():N}";

            // FechaInicio anterior al corte del filtro opcional: debe excluirse aunque siga
            // vigente por FechaFin.
            await SeedEventAsync(BuildEvent(
                antesDelCorte, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: baseFecha,
                fechaFin: baseFecha.AddDays(5)));

            // FechaInicio posterior al corte: debe incluirse.
            await SeedEventAsync(BuildEvent(
                despuesDelCorte, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: baseFecha.AddDays(10),
                fechaFin: baseFecha.AddDays(15)));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto { FechaDesde = corte, Limit = 50 });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.DoesNotContain(antesDelCorte, ids);
            Assert.Contains(despuesDelCorte, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_SoloFechaHasta_ExcludesFechaInicioOnOrAfterHasta()
        {
            // Ventana de fechas exclusiva de este test (día +90).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var hasta = DateTime.UtcNow.AddDays(90);

            var antesDeHasta = $"event-antes-hasta-{Guid.NewGuid():N}";
            var despuesDeHasta = $"event-despues-hasta-{Guid.NewGuid():N}";

            // FechaInicio antes del límite superior (nunca exactamente igual: ver comentario de
            // SearchEventsAsync_PaginatesAcrossPages... sobre el bug de inclusividad conocido del
            // emulador local en el límite exacto de una consulta con desigualdades sobre dos
            // campos distintos): debe incluirse, Hasta es exclusiva.
            await SeedEventAsync(BuildEvent(
                antesDeHasta, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: hasta.AddSeconds(-1),
                fechaFin: hasta.AddDays(5)));

            // FechaInicio en/después del límite superior: debe excluirse.
            await SeedEventAsync(BuildEvent(
                despuesDeHasta, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: hasta.AddSeconds(1),
                fechaFin: hasta.AddDays(5)));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto { FechaHasta = hasta, Limit = 50 });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.Contains(antesDeHasta, ids);
            Assert.DoesNotContain(despuesDeHasta, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_RangoCompleto_DesdeInclusive_HastaExclusive()
        {
            // Ventana de fechas exclusiva de este test (día +100).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var desde = DateTime.UtcNow.AddDays(100);
            var hasta = desde.AddDays(3);

            var antesDeDesde = $"event-antes-desde-{Guid.NewGuid():N}";
            var dentroDelRango = $"event-dentro-rango-{Guid.NewGuid():N}";
            var enOTrasHasta = $"event-en-hasta-{Guid.NewGuid():N}";

            // Antes del límite inferior: excluido.
            await SeedEventAsync(BuildEvent(
                antesDeDesde, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: desde.AddSeconds(-1),
                fechaFin: hasta.AddDays(5)));

            // Dentro del rango: incluido.
            await SeedEventAsync(BuildEvent(
                dentroDelRango, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: desde.AddSeconds(1),
                fechaFin: hasta.AddDays(5)));

            // En/después del límite superior (exclusivo): excluido.
            await SeedEventAsync(BuildEvent(
                enOTrasHasta, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: hasta.AddSeconds(1),
                fechaFin: hasta.AddDays(5)));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto { FechaDesde = desde, FechaHasta = hasta, Limit = 50 });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.DoesNotContain(antesDeDesde, ids);
            Assert.Contains(dentroDelRango, ids);
            Assert.DoesNotContain(enOTrasHasta, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_FechaDesdeMayorQueFechaHasta_ThrowsEventValidationException()
        {
            var sut = CreateSut(_fixture);
            var desde = DateTime.UtcNow.AddDays(10);
            var hasta = DateTime.UtcNow.AddDays(5);

            var ex = await Assert.ThrowsAsync<EventValidationException>(() =>
                sut.SearchEventsAsync(new EventSearchFilterDto { FechaDesde = desde, FechaHasta = hasta, Limit = 50 }));

            Assert.Contains("Desde", ex.Message);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_PaginatesAcrossPages_WithoutLosingOrRepeatingEvents_RespectingBothFilters()
        {
            // Ventana de fechas exclusiva de este test (día +300).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var baseFecha = DateTime.UtcNow.AddDays(300);
            var corteFechaInicio = baseFecha.AddDays(1);

            // Offsets estrictamente > 0 (nunca == corteFechaInicio): la Firestore Emulator local
            // tiene un bug de inclusividad conocido en el límite exacto de una consulta con
            // desigualdades sobre dos campos distintos (confirmado por reproducción manual);
            // producción sí resuelve correctamente el límite inclusivo de ">=", pero se evita acá
            // para que la prueba no dependa de esa discrepancia del emulador local.
            var incluidos = new List<string>();
            for (int i = 1; i <= 5; i++)
            {
                var id = $"event-pag-{i}-{Guid.NewGuid():N}";
                incluidos.Add(id);
                await SeedEventAsync(BuildEvent(
                    id, personaId,
                    estado: Event.EventStatus.Publicado,
                    fechaInicio: corteFechaInicio.AddHours(i), // > corte: cumple el filtro opcional
                    fechaFin: baseFecha.AddDays(10).AddHours(i)));
            }

            // No cumple el filtro opcional de FechaInicio: no debe aparecer en ninguna página.
            var excluidoPorFechaInicio = $"event-excluido-fi-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                excluidoPorFechaInicio, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: corteFechaInicio.AddHours(-1),
                fechaFin: baseFecha.AddDays(10)));

            // Cumple ambos filtros de fecha pero no está Publicado: no debe aparecer.
            var excluidoPorEstado = $"event-excluido-estado-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                excluidoPorEstado, personaId,
                estado: Event.EventStatus.Cancelado,
                fechaInicio: corteFechaInicio.AddHours(2),
                fechaFin: baseFecha.AddDays(10)));

            var collected = new List<EventResponse>();
            string? cursor = null;
            var pageCount = 0;
            const int safetyLimit = 10; // evita colgar el test si algo en la paginación falla

            do
            {
                var page = await sut.SearchEventsAsync(new EventSearchFilterDto
                {
                    FechaDesde = corteFechaInicio,
                    Limit = 2,
                    LastEventId = cursor
                });

                Assert.True(page.Data.Count() <= 2, "Ninguna página debe exceder el tamaño pedido.");

                collected.AddRange(page.Data);
                cursor = page.LastDocumentId;
                pageCount++;

                if (!page.HasNextPage) break;

                Assert.NotNull(cursor); // una página con HasNextPage=true siempre debe traer cursor
            } while (pageCount < safetyLimit);

            var collectedIds = collected.Select(e => e.Id).ToList();

            // No pierde ni repite eventos entre páginas, y respeta ambos filtros: exactamente los
            // 5 eventos incluidos, ni más ni menos.
            Assert.Equal(incluidos.Count, collectedIds.Distinct().Count());
            Assert.Equal(incluidos.OrderBy(x => x).ToList(), collectedIds.OrderBy(x => x).ToList());
            Assert.DoesNotContain(excluidoPorFechaInicio, collectedIds);
            Assert.DoesNotContain(excluidoPorEstado, collectedIds);
            Assert.Equal(3, pageCount); // 5 elementos con Limit=2 -> páginas de 2, 2 y 1
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_EventsWithSameFechas_AreOrderedDeterministicallyByDocumentId()
        {
            // Ventana de fechas exclusiva de este test (día +400).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var fechaInicio = DateTime.UtcNow.AddDays(400);
            var fechaFin = fechaInicio.AddDays(1);

            // Ids elegidos para que el orden lexicográfico esperado sea conocido de antemano.
            var idA = $"aaa-tie-{Guid.NewGuid():N}";
            var idB = $"bbb-tie-{Guid.NewGuid():N}";

            try
            {
                // Se siembran en orden inverso al esperado para confirmar que el orden de
                // resultado depende del id de documento, no del orden de inserción.
                await SeedEventAsync(BuildEvent(idB, personaId, estado: Event.EventStatus.Publicado, fechaInicio: fechaInicio, fechaFin: fechaFin));
                await SeedEventAsync(BuildEvent(idA, personaId, estado: Event.EventStatus.Publicado, fechaInicio: fechaInicio, fechaFin: fechaFin));

                var filter = new EventSearchFilterDto { Limit = 50 };
                var firstRun = (await sut.SearchEventsAsync(filter)).Data.Select(e => e.Id).Where(id => id == idA || id == idB).ToList();
                var secondRun = (await sut.SearchEventsAsync(filter)).Data.Select(e => e.Id).Where(id => id == idA || id == idB).ToList();

                Assert.Equal(new[] { idA, idB }, firstRun);
                Assert.Equal(firstRun, secondRun);
            }
            finally
            {
                // Comportamiento observado (no un bug confirmado de Firestore) en el Firestore
                // Emulator local usado por esta suite: dejar dos documentos con una clave de
                // índice compuesto (FechaFin, FechaInicio) idéntica pareció afectar resultados de
                // consultas posteriores de SearchEventsAsync en la misma colección, incluso con
                // ventanas de fecha distintas. No se investigó más a fondo la causa. Se limpia
                // acá por aislamiento de test, para que este test no dependa de ni afecte el
                // orden de ejecución de los demás.
                await _fixture.Db!.Collection("events").Document(idA).DeleteAsync();
                await _fixture.Db!.Collection("events").Document(idB).DeleteAsync();
            }
        }

        // ---- SearchEventsAsync: cobertura de las cuatro combinaciones de filtros de igualdad
        // opcionales (Categoria/Ubicacion) contra los cuatro índices compuestos explícitos de
        // firestore.indexes.json. Estas pruebas validan la LÓGICA de filtrado del servicio contra
        // el Firestore Emulator; el emulador no valida que un índice de producción exista para
        // cada forma de consulta (no falla por índice faltante como lo haría producción), así que
        // no acreditan por sí solas que firestore.indexes.json esté desplegado correctamente en
        // producción.

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_SinCategoriaNiUbicacion_IgnoraEsosCamposYRespetaVigencia()
        {
            // Ventana de fechas exclusiva de este test (día +50). Deliberadamente por debajo del
            // día +301 usado por SearchEventsAsync_PaginatesAcrossPages...: ese test filtra por
            // FechaInicio >= día +301 sin cota superior, así que cualquier evento "visible" de
            // este archivo con FechaInicio mayor a esa cota se colaría en sus resultados.
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var baseFecha = DateTime.UtcNow.AddDays(50);

            var visible = $"event-visible-{Guid.NewGuid():N}";
            var finalizado = $"event-finalizado-{Guid.NewGuid():N}";
            var cancelado = $"event-cancelado-{Guid.NewGuid():N}";

            await SeedEventAsync(BuildEvent(visible, personaId, estado: Event.EventStatus.Publicado, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(5), categoria: Event.EventCategory.Otros, ubicacion: "Cualquier lugar"));
            // FechaFin real ya pasada (no relativa a baseFecha): Finalizado de verdad.
            await SeedEventAsync(BuildEvent(finalizado, personaId, estado: Event.EventStatus.Publicado, fechaInicio: DateTime.UtcNow.AddDays(-3), fechaFin: DateTime.UtcNow.AddDays(-1)));
            await SeedEventAsync(BuildEvent(cancelado, personaId, estado: Event.EventStatus.Cancelado, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(5)));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto { Limit = 50 });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.Contains(visible, ids);
            Assert.DoesNotContain(finalizado, ids);
            Assert.DoesNotContain(cancelado, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_SoloCategoria_FiltraPorCategoria_CombinaConFechaInicioYFechaFin()
        {
            // Ventana de fechas exclusiva de este test (día +60), por la misma razón que el test
            // anterior (queda por debajo del día +301 de la prueba de paginación).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var baseFecha = DateTime.UtcNow.AddDays(60);
            var corteFechaInicio = baseFecha.AddDays(1);

            var matchCategoria = $"event-match-cat-{Guid.NewGuid():N}";
            var otraCategoria = $"event-otra-cat-{Guid.NewGuid():N}";
            var antesDelCorte = $"event-antes-corte-cat-{Guid.NewGuid():N}";
            var finalizadoMismaCategoria = $"event-finalizado-cat-{Guid.NewGuid():N}";

            // Cumple Categoria, FechaInicio (filtro opcional) y vigencia: debe incluirse.
            await SeedEventAsync(BuildEvent(
                matchCategoria, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: corteFechaInicio.AddHours(1),
                fechaFin: baseFecha.AddDays(10),
                categoria: Event.EventCategory.Tecnologia));

            // Categoria distinta: excluido por el filtro de igualdad.
            await SeedEventAsync(BuildEvent(
                otraCategoria, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: corteFechaInicio.AddHours(1),
                fechaFin: baseFecha.AddDays(10),
                categoria: Event.EventCategory.Musica));

            // Misma Categoria pero FechaInicio anterior al corte: excluido por el filtro opcional.
            await SeedEventAsync(BuildEvent(
                antesDelCorte, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: corteFechaInicio.AddHours(-1),
                fechaFin: baseFecha.AddDays(10),
                categoria: Event.EventCategory.Tecnologia));

            // Misma Categoria, ya Finalizado (FechaFin real ya pasada): excluido por vigencia.
            await SeedEventAsync(BuildEvent(
                finalizadoMismaCategoria, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-3),
                fechaFin: DateTime.UtcNow.AddDays(-1),
                categoria: Event.EventCategory.Tecnologia));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto
            {
                Categoria = Event.EventCategory.Tecnologia,
                FechaDesde = corteFechaInicio,
                Limit = 50
            });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.Contains(matchCategoria, ids);
            Assert.DoesNotContain(otraCategoria, ids);
            Assert.DoesNotContain(antesDelCorte, ids);
            Assert.DoesNotContain(finalizadoMismaCategoria, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_SoloUbicacion_FiltraPorUbicacion_CombinaConFechaFin()
        {
            // Ventana de fechas exclusiva de este test (día +70).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var baseFecha = DateTime.UtcNow.AddDays(70);
            var ubicacionObjetivo = $"Ubicacion-{Guid.NewGuid():N}";
            var otraUbicacion = $"Otra-Ubicacion-{Guid.NewGuid():N}";

            var matchUbicacion = $"event-match-ubi-{Guid.NewGuid():N}";
            var otraUbicacionEvento = $"event-otra-ubi-{Guid.NewGuid():N}";
            var finalizadoMismaUbicacion = $"event-finalizado-ubi-{Guid.NewGuid():N}";

            // Cumple Ubicacion y vigencia: debe incluirse.
            await SeedEventAsync(BuildEvent(
                matchUbicacion, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: baseFecha,
                fechaFin: baseFecha.AddDays(5),
                ubicacion: ubicacionObjetivo));

            // Ubicacion distinta: excluido por el filtro de igualdad.
            await SeedEventAsync(BuildEvent(
                otraUbicacionEvento, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: baseFecha,
                fechaFin: baseFecha.AddDays(5),
                ubicacion: otraUbicacion));

            // Misma Ubicacion, ya Finalizado (FechaFin real ya pasada): excluido por vigencia.
            await SeedEventAsync(BuildEvent(
                finalizadoMismaUbicacion, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-3),
                fechaFin: DateTime.UtcNow.AddDays(-1),
                ubicacion: ubicacionObjetivo));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto { Ubicacion = ubicacionObjetivo, Limit = 50 });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.Contains(matchUbicacion, ids);
            Assert.DoesNotContain(otraUbicacionEvento, ids);
            Assert.DoesNotContain(finalizadoMismaUbicacion, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_CategoriaYUbicacionJuntas_FiltraPorAmbasYRespetaVigencia()
        {
            // Ventana de fechas exclusiva de este test (día +80).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var baseFecha = DateTime.UtcNow.AddDays(80);
            var ubicacionObjetivo = $"Ubicacion-{Guid.NewGuid():N}";
            var otraUbicacion = $"Otra-Ubicacion-{Guid.NewGuid():N}";

            var matchAmbos = $"event-match-ambos-{Guid.NewGuid():N}";
            var soloCategoria = $"event-solo-cat-{Guid.NewGuid():N}";
            var soloUbicacion = $"event-solo-ubi-{Guid.NewGuid():N}";
            var finalizadoAmbos = $"event-finalizado-ambos-{Guid.NewGuid():N}";

            // Cumple Categoria, Ubicacion y vigencia: debe incluirse.
            await SeedEventAsync(BuildEvent(
                matchAmbos, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: baseFecha,
                fechaFin: baseFecha.AddDays(5),
                categoria: Event.EventCategory.Deportes,
                ubicacion: ubicacionObjetivo));

            // Categoria correcta pero Ubicacion distinta: excluido.
            await SeedEventAsync(BuildEvent(
                soloCategoria, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: baseFecha,
                fechaFin: baseFecha.AddDays(5),
                categoria: Event.EventCategory.Deportes,
                ubicacion: otraUbicacion));

            // Ubicacion correcta pero Categoria distinta: excluido.
            await SeedEventAsync(BuildEvent(
                soloUbicacion, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: baseFecha,
                fechaFin: baseFecha.AddDays(5),
                categoria: Event.EventCategory.Arte,
                ubicacion: ubicacionObjetivo));

            // Categoria y Ubicacion correctas, ya Finalizado (FechaFin real ya pasada): excluido
            // por vigencia.
            await SeedEventAsync(BuildEvent(
                finalizadoAmbos, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-3),
                fechaFin: DateTime.UtcNow.AddDays(-1),
                categoria: Event.EventCategory.Deportes,
                ubicacion: ubicacionObjetivo));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto
            {
                Categoria = Event.EventCategory.Deportes,
                Ubicacion = ubicacionObjetivo,
                Limit = 50
            });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.Contains(matchAmbos, ids);
            Assert.DoesNotContain(soloCategoria, ids);
            Assert.DoesNotContain(soloUbicacion, ids);
            Assert.DoesNotContain(finalizadoAmbos, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_CategoriaUbicacionYRango_CombinanTodosLosFiltrosEnLaMismaConsulta()
        {
            // Ventana de fechas exclusiva de este test (día +110).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var ubicacionObjetivo = $"Ubicacion-{Guid.NewGuid():N}";
            var otraUbicacion = $"Otra-Ubicacion-{Guid.NewGuid():N}";
            var desde = DateTime.UtcNow.AddDays(110);
            var hasta = desde.AddDays(3);

            var matchTodos = $"event-match-todos-{Guid.NewGuid():N}";
            var otraCategoria = $"event-otra-cat-todos-{Guid.NewGuid():N}";
            var otraUbicacionEvento = $"event-otra-ubi-todos-{Guid.NewGuid():N}";
            var fueraDeRango = $"event-fuera-rango-todos-{Guid.NewGuid():N}";

            // Cumple Categoria, Ubicacion y rango: debe incluirse.
            await SeedEventAsync(BuildEvent(
                matchTodos, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: desde.AddSeconds(1),
                fechaFin: hasta.AddDays(5),
                categoria: Event.EventCategory.Arte,
                ubicacion: ubicacionObjetivo));

            // Categoria distinta: excluido por el filtro de igualdad.
            await SeedEventAsync(BuildEvent(
                otraCategoria, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: desde.AddSeconds(1),
                fechaFin: hasta.AddDays(5),
                categoria: Event.EventCategory.Otros,
                ubicacion: ubicacionObjetivo));

            // Ubicacion distinta: excluido por el filtro de igualdad.
            await SeedEventAsync(BuildEvent(
                otraUbicacionEvento, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: desde.AddSeconds(1),
                fechaFin: hasta.AddDays(5),
                categoria: Event.EventCategory.Arte,
                ubicacion: otraUbicacion));

            // Categoria y Ubicacion correctas, pero FechaInicio en/después del límite superior
            // (exclusivo): excluido por el rango.
            await SeedEventAsync(BuildEvent(
                fueraDeRango, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: hasta.AddSeconds(1),
                fechaFin: hasta.AddDays(5),
                categoria: Event.EventCategory.Arte,
                ubicacion: ubicacionObjetivo));

            var result = await sut.SearchEventsAsync(new EventSearchFilterDto
            {
                Categoria = Event.EventCategory.Arte,
                Ubicacion = ubicacionObjetivo,
                FechaDesde = desde,
                FechaHasta = hasta,
                Limit = 50
            });
            var ids = result.Data.Select(e => e.Id).ToList();

            Assert.Contains(matchTodos, ids);
            Assert.DoesNotContain(otraCategoria, ids);
            Assert.DoesNotContain(otraUbicacionEvento, ids);
            Assert.DoesNotContain(fueraDeRango, ids);
        }

        [FirestoreEmulatorFact]
        public async Task SearchEventsAsync_PaginatesAcrossPages_WithCategoriaUbicacionAndFullDateRange_NoLossOrDuplicates()
        {
            // Ventana de fechas exclusiva de este test (día +130). Deliberadamente por debajo del
            // día +301 usado por SearchEventsAsync_PaginatesAcrossPages_WithoutLosingOrRepeating...:
            // ese test filtra solo por FechaDesde >= día +301, sin cota superior ni Categoria/
            // Ubicacion, así que cualquier evento "visible" de este archivo con FechaInicio mayor
            // a esa cota se colaría en su conteo estricto de resultados (ya le pasó a este mismo
            // test cuando usaba el día +320: rompió esa otra prueba).
            var sut = CreateSut(_fixture);
            var personaId = $"persona-{Guid.NewGuid():N}";
            var ubicacionObjetivo = $"Ubicacion-{Guid.NewGuid():N}";
            var desde = DateTime.UtcNow.AddDays(130);
            var hasta = desde.AddDays(3);

            var incluidos = new List<string>();
            for (int i = 1; i <= 5; i++)
            {
                var id = $"event-pag-todos-{i}-{Guid.NewGuid():N}";
                incluidos.Add(id);
                await SeedEventAsync(BuildEvent(
                    id, personaId,
                    estado: Event.EventStatus.Publicado,
                    fechaInicio: desde.AddSeconds(i), // dentro del rango [desde, hasta)
                    fechaFin: hasta.AddDays(10),
                    categoria: Event.EventCategory.Deportes,
                    ubicacion: ubicacionObjetivo));
            }

            // Fuera del rango de fechas: no debe aparecer en ninguna página.
            var fueraDeRango = $"event-pag-fuera-rango-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                fueraDeRango, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: hasta.AddSeconds(1),
                fechaFin: hasta.AddDays(10),
                categoria: Event.EventCategory.Deportes,
                ubicacion: ubicacionObjetivo));

            // Categoria distinta, dentro del rango: no debe aparecer en ninguna página.
            var otraCategoria = $"event-pag-otra-cat-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(
                otraCategoria, personaId,
                estado: Event.EventStatus.Publicado,
                fechaInicio: desde.AddSeconds(1),
                fechaFin: hasta.AddDays(10),
                categoria: Event.EventCategory.Musica,
                ubicacion: ubicacionObjetivo));

            var collected = new List<EventResponse>();
            string? cursor = null;
            var pageCount = 0;
            const int safetyLimit = 10;

            do
            {
                var page = await sut.SearchEventsAsync(new EventSearchFilterDto
                {
                    Categoria = Event.EventCategory.Deportes,
                    Ubicacion = ubicacionObjetivo,
                    FechaDesde = desde,
                    FechaHasta = hasta,
                    Limit = 2,
                    LastEventId = cursor
                });

                Assert.True(page.Data.Count() <= 2, "Ninguna página debe exceder el tamaño pedido.");

                collected.AddRange(page.Data);
                cursor = page.LastDocumentId;
                pageCount++;

                if (!page.HasNextPage) break;

                Assert.NotNull(cursor);
            } while (pageCount < safetyLimit);

            var collectedIds = collected.Select(e => e.Id).ToList();

            Assert.Equal(incluidos.Count, collectedIds.Distinct().Count());
            Assert.Equal(incluidos.OrderBy(x => x).ToList(), collectedIds.OrderBy(x => x).ToList());
            Assert.DoesNotContain(fueraDeRango, collectedIds);
            Assert.DoesNotContain(otraCategoria, collectedIds);
            Assert.Equal(3, pageCount); // 5 elementos con Limit=2 -> páginas de 2, 2 y 1
        }

        // ---- GetOwnedByIdAsync (organizer/{id}): cualquier estado propio ----

        [FirestoreEmulatorFact]
        public async Task GetOwnedByIdAsync_OwnEventInBorrador_ReturnsEventResponse()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Borrador));

            var response = await sut.GetOwnedByIdAsync(eventId, duenoUid);

            Assert.Equal(Event.EventEffectiveStatus.Borrador, response.Estado);
            // Detalle autenticado del organizador (GET /api/events/organizer/{id}): también
            // expone el TicketTypeId real.
            Assert.False(string.IsNullOrEmpty(Assert.Single(response.TicketGroups).Id));
        }

        [FirestoreEmulatorFact]
        public async Task GetOwnedByIdAsync_ForeignEvent_ThrowsEventOwnershipException()
        {
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var extranioUid = $"uid-extranio-{Guid.NewGuid():N}";
            var extranioPersonaId = $"persona-extranio-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (extranioUid, extranioPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, duenoPersonaId, estado: Event.EventStatus.Borrador));

            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.GetOwnedByIdAsync(eventId, extranioUid));
        }

        [FirestoreEmulatorFact]
        public async Task GetOwnedByIdAsync_Nonexistent_ThrowsEventNotFoundException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));

            await Assert.ThrowsAsync<EventNotFoundException>(() => sut.GetOwnedByIdAsync($"event-inexistente-{Guid.NewGuid():N}", uid));
        }

        // ---- GetByOrganizerIdAsync: panel propio, cualquier estado, sin filtro de vigencia ----

        [FirestoreEmulatorFact]
        public async Task GetByOrganizerIdAsync_FiltersByOrganizadorPersonaId()
        {
            var personaAUid = $"uid-a-{Guid.NewGuid():N}";
            var personaAId = $"persona-a-{Guid.NewGuid():N}";
            var personaBId = $"persona-b-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (personaAUid, personaAId));

            var eventoA1 = $"event-a1-{Guid.NewGuid():N}";
            var eventoA2 = $"event-a2-{Guid.NewGuid():N}";
            var eventoB1 = $"event-b1-{Guid.NewGuid():N}";

            await SeedEventAsync(BuildEvent(eventoA1, personaAId));
            await SeedEventAsync(BuildEvent(eventoA2, personaAId));
            await SeedEventAsync(BuildEvent(eventoB1, personaBId));

            var propios = (await sut.GetByOrganizerIdAsync(personaAUid)).ToList();
            var ids = propios.Select(e => e.Id).ToList();

            Assert.Equal(2, propios.Count);
            Assert.Contains(eventoA1, ids);
            Assert.Contains(eventoA2, ids);
            Assert.DoesNotContain(eventoB1, ids);

            // Lista de eventos propios (GET /api/events/organizer/me): también expone el
            // TicketTypeId real de cada evento.
            Assert.All(propios, e => Assert.All(e.TicketGroups, t => Assert.False(string.IsNullOrEmpty(t.Id))));
        }
    }
}
