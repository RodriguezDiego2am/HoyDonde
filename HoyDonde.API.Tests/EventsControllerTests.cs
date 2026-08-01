using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class EventsControllerTests : IClassFixture<TestApplicationFactory>
    {
        private readonly TestApplicationFactory _factory;
        private readonly System.Net.Http.HttpClient _client;

        public EventsControllerTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            // Etapa 5 del refactor de seguridad: la autorización real depende de IPermissionService,
            // no del claim de rol legacy. "test-uid-123" (el uid por default de FakeAuthHandler)
            // recibe acá todas las acciones que este controller necesita para sus tests de
            // "camino feliz"; los tests de "sin acción" usan un uid distinto (Test-Uid) que nunca
            // se concede acá.
            _factory.GrantAccion("test-uid-123", "usuario-events-test", "persona-events-test",
                Acciones.EventoCrear, Acciones.EventoEditarPropio, Acciones.EventoPublicarPropio,
                Acciones.EventoCancelarPropio, Acciones.EventoVerPropios);
        }

        [Fact]
        public async Task CreateEvent_WithValidOrganizer_ReturnsOk()
        {
            // Arrange
            var request = new EventCreateRequest
            {
                Nombre = "Festival de Prueba",
                Descripcion = "Un evento de prueba",
                FechaInicio = DateTime.UtcNow.AddDays(5),
                Ubicacion = "La Plaza",
                Categoria = Event.EventCategory.Musica,
                TicketGroups = new List<TicketGroupDto>
                {
                    new TicketGroupDto { Nombre = "General", Precio = 50, CantidadDisponible = 100 }
                }
            };

            var expectedResponse = new EventResponse
            {
                Id = "new-event-id",
                Nombre = "Festival de Prueba",
                Estado = Event.EventStatus.Activo
            };

            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), "test-uid-123"))
                .ReturnsAsync(expectedResponse);

            // Act
            var response = await _client.PostAsJsonAsync("/api/events", request);

            // Assert
            var content = await response.Content.ReadAsStringAsync();
            if(!response.IsSuccessStatusCode) throw new Exception(content);

            response.EnsureSuccessStatusCode(); 
            var result = await response.Content.ReadFromJsonAsync<EventResponse>();
            Assert.NotNull(result);
            Assert.Equal("new-event-id", result.Id);
            Assert.Equal("Festival de Prueba", result.Nombre);
        }

        [Fact]
        public async Task CreateEvent_WithServiceError_ReturnsBadRequest()
        {
            // Arrange
            var request = new EventCreateRequest
            {
                Nombre = "Festival Roto",
                FechaInicio = DateTime.UtcNow.AddDays(5),
            };

            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Organizer not found or missing fields"));

            // Act
            var response = await _client.PostAsJsonAsync("/api/events", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.InternalServerError)
                throw new Exception("Server returned 500: " + content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---- Ownership: publicar/cancelar/actualizar sólo el propio evento ----

        [Fact]
        public async Task PublishEvent_OwnEvent_ReturnsOk()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("event-own", "test-uid-123"))
                .Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/events/event-own/publish", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task PublishEvent_ForeignEvent_ReturnsForbidden()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("event-foreign", "test-uid-123"))
                .ThrowsAsync(new EventOwnershipException("event-foreign", "test-uid-123"));

            var response = await _client.PostAsync("/api/events/event-foreign/publish", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task PublishEvent_NonexistentEvent_ReturnsNotFound()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("missing-event", "test-uid-123"))
                .ThrowsAsync(new EventNotFoundException("missing-event"));

            var response = await _client.PostAsync("/api/events/missing-event/publish", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CancelEvent_OwnEvent_ReturnsOk()
        {
            _factory.MockEventService
                .Setup(s => s.CancelEventAsync("event-own", "test-uid-123"))
                .Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/events/event-own/cancel", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task CancelEvent_ForeignEvent_ReturnsForbidden()
        {
            _factory.MockEventService
                .Setup(s => s.CancelEventAsync("event-foreign", "test-uid-123"))
                .ThrowsAsync(new EventOwnershipException("event-foreign", "test-uid-123"));

            var response = await _client.PostAsync("/api/events/event-foreign/cancel", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_OwnEvent_ReturnsOk()
        {
            var request = new EventUpdateRequest
            {
                Nombre = "Festival Actualizado",
                Descripcion = "Descripcion actualizada",
                FechaInicio = DateTime.UtcNow.AddDays(10),
                Ubicacion = "Nueva Ubicacion",
                Categoria = Event.EventCategory.Musica
            };

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-own", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ReturnsAsync(new EventResponse { Id = "event-own", Nombre = request.Nombre });

            var response = await _client.PutAsJsonAsync("/api/events/event-own", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_ForeignEvent_ReturnsForbidden()
        {
            var request = new EventUpdateRequest
            {
                Nombre = "Festival Ajeno",
                FechaInicio = DateTime.UtcNow.AddDays(10)
            };

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-foreign", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ThrowsAsync(new EventOwnershipException("event-foreign", "test-uid-123"));

            var response = await _client.PutAsJsonAsync("/api/events/event-foreign", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- IdentityNotProvisionedException: 403 genérico, nunca 400, nunca el UID en el body ----

        [Fact]
        public async Task CreateEvent_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), "test-uid-123"))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var request = new EventCreateRequest
            {
                Nombre = "Festival sin identidad",
                FechaInicio = DateTime.UtcNow.AddDays(5),
            };

            var response = await _client.PostAsJsonAsync("/api/events", request);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("test-uid-123", content);
        }

        [Fact]
        public async Task PublishEvent_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("event-sin-identidad", "test-uid-123"))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var response = await _client.PostAsync("/api/events/event-sin-identidad/publish", null);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("test-uid-123", content);
        }

        [Fact]
        public async Task GetMyEvents_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            // GetMyEvents no tiene ningún try/catch propio: cubre el camino en el que la
            // excepción llega directo al middleware, sin pasar por ningún catch de controller.
            _factory.MockEventService
                .Setup(s => s.GetByOrganizerIdAsync("test-uid-123"))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var response = await _client.GetAsync("/api/events/organizer/me");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("test-uid-123", content);
        }

        // ---- Etapa 5: mapeo de policy por endpoint (sin la acción -> 403) ----

        private static HttpRequestMessage RequestSinAccion(HttpMethod method, string path)
        {
            var msg = new HttpRequestMessage(method, path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-events");
            return msg;
        }

        [Fact]
        public async Task CreateEvent_SinAccionEventoCrear_ReturnsForbidden()
        {
            var request = new EventCreateRequest { Nombre = "X", FechaInicio = DateTime.UtcNow.AddDays(5) };
            var msg = RequestSinAccion(HttpMethod.Post, "/api/events");
            msg.Content = JsonContent.Create(request);

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task PublishEvent_SinAccionEventoPublicarPropio_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Post, "/api/events/event-1/publish"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task CancelEvent_SinAccionEventoCancelarPropio_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Post, "/api/events/event-1/cancel"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_SinAccionEventoEditarPropio_ReturnsForbidden()
        {
            var request = new EventUpdateRequest { Nombre = "X", FechaInicio = DateTime.UtcNow.AddDays(5) };
            var msg = RequestSinAccion(HttpMethod.Put, "/api/events/event-1");
            msg.Content = JsonContent.Create(request);

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetMyEvents_SinAccionEventoVerPropios_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Get, "/api/events/organizer/me"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- Etapa 5: los endpoints públicos siguen siendo anónimos ----

        [Fact]
        public async Task GetEvent_Anonymous_ReturnsOkOrNotFound_NeverUnauthorized()
        {
            _factory.MockEventService.Setup(s => s.GetByIdAsync("event-publico")).ReturnsAsync((Event?)null);

            var anonClient = _factory.CreateClient(); // sin Authorization header
            var response = await anonClient.GetAsync("/api/events/event-publico");

            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task SearchEvents_Anonymous_ReturnsOk()
        {
            _factory.MockEventService
                .Setup(s => s.SearchEventsAsync(It.IsAny<EventSearchFilterDto>()))
                .ReturnsAsync(new PagedResponse<Event> { Data = new List<Event>(), HasNextPage = false });

            var anonClient = _factory.CreateClient(); // sin Authorization header
            var response = await anonClient.GetAsync("/api/events");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
