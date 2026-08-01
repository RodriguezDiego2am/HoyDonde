using System;
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
    // docs/security-refactor-plan.md §4, Etapa 4: Event.OrganizadorPersonaId contra Firestore
    // Emulator real. El actor (UID de Firebase) se resuelve a PersonaId vía un
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

        [FirestoreEmulatorFact]
        public async Task CreateEventAsync_PersistsOrganizadorPersonaId_NotUid()
        {
            var organizadorUid = $"uid-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (organizadorUid, organizadorPersonaId));

            var request = new EventCreateRequest
            {
                Nombre = "Evento de prueba",
                Descripcion = "Descripcion",
                FechaInicio = DateTime.UtcNow.AddDays(5),
                Ubicacion = "Buenos Aires",
                Categoria = Event.EventCategory.Musica,
            };

            var response = await sut.CreateEventAsync(request, organizadorUid);

            var snapshot = await _fixture.Db!.Collection("events").Document(response.Id).GetSnapshotAsync();
            Assert.True(snapshot.Exists);
            var evento = snapshot.ConvertTo<Event>();

            Assert.Equal(organizadorPersonaId, evento.OrganizadorPersonaId);
            Assert.NotEqual(organizadorUid, evento.OrganizadorPersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task PublishEventAsync_ForForeignEvent_ThrowsEventOwnershipException()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var extranioUid = $"uid-extranio-{Guid.NewGuid():N}";
            var extranioPersonaId = $"persona-extranio-{Guid.NewGuid():N}";

            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId), (extranioUid, extranioPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento ajeno",
                Fecha = DateTime.UtcNow.AddDays(5),
                OrganizadorPersonaId = duenoPersonaId,
                Estado = Event.EventStatus.Activo,
            });

            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.PublishEventAsync(eventId, extranioUid));
            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.CancelEventAsync(eventId, extranioUid));
            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.UpdateEventAsync(eventId, extranioUid, new EventUpdateRequest
            {
                Nombre = "Intento ajeno",
                FechaInicio = DateTime.UtcNow.AddDays(1),
            }));
        }

        [FirestoreEmulatorFact]
        public async Task PublishEventAsync_OwnEvent_Succeeds()
        {
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento propio",
                Fecha = DateTime.UtcNow.AddDays(5),
                OrganizadorPersonaId = duenoPersonaId,
                Estado = Event.EventStatus.Activo,
            });

            await sut.PublishEventAsync(eventId, duenoUid);

            var snapshot = await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync();
            Assert.Equal(Event.EventStatus.Publicado, snapshot.ConvertTo<Event>().Estado);
        }

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

            var fecha = DateTime.UtcNow.AddDays(5);
            await _fixture.Db!.Collection("events").Document(eventoA1).SetAsync(new Event { Id = eventoA1, Nombre = "A1", Fecha = fecha, OrganizadorPersonaId = personaAId });
            await _fixture.Db!.Collection("events").Document(eventoA2).SetAsync(new Event { Id = eventoA2, Nombre = "A2", Fecha = fecha, OrganizadorPersonaId = personaAId });
            await _fixture.Db!.Collection("events").Document(eventoB1).SetAsync(new Event { Id = eventoB1, Nombre = "B1", Fecha = fecha, OrganizadorPersonaId = personaBId });

            var propios = (await sut.GetByOrganizerIdAsync(personaAUid)).ToList();

            Assert.Equal(2, propios.Count);
            Assert.All(propios, e => Assert.Equal(personaAId, e.OrganizadorPersonaId));
            Assert.DoesNotContain(propios, e => e.Id == eventoB1);
        }
    }
}
