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

        private static EventCreateRequest ValidCreateRequest() => new EventCreateRequest
        {
            Nombre = "Festival de Prueba",
            Descripcion = "Un evento de prueba",
            FechaInicio = DateTime.UtcNow.AddDays(5),
            FechaFin = DateTime.UtcNow.AddDays(6),
            Ubicacion = "La Plaza",
            Categoria = Event.EventCategory.Musica,
            TicketGroups = new List<TicketGroupDto>
            {
                new TicketGroupDto { Nombre = "General", Precio = 50, CantidadDisponible = 100 }
            }
        };

        private static EventUpdateRequest ValidUpdateRequest() => new EventUpdateRequest
        {
            Nombre = "Festival Actualizado",
            Descripcion = "Descripcion actualizada",
            FechaInicio = DateTime.UtcNow.AddDays(10),
            FechaFin = DateTime.UtcNow.AddDays(11),
            Ubicacion = "Nueva Ubicacion",
            Categoria = Event.EventCategory.Musica,
            TicketGroups = new List<TicketGroupDto>
            {
                new TicketGroupDto { Nombre = "General", Precio = 50, CantidadDisponible = 100 }
            }
        };

        [Fact]
        public async Task CreateEvent_WithValidOrganizer_ReturnsOk()
        {
            var request = ValidCreateRequest();

            var expectedResponse = new EventResponse
            {
                Id = "new-event-id",
                Nombre = "Festival de Prueba",
                Estado = Event.EventEffectiveStatus.Borrador
            };

            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), "test-uid-123"))
                .ReturnsAsync(expectedResponse);

            var response = await _client.PostAsJsonAsync("/api/events", request);

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception(content);

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EventResponse>();
            Assert.NotNull(result);
            Assert.Equal("new-event-id", result!.Id);
            Assert.Equal("Festival de Prueba", result.Nombre);
        }

        [Fact]
        public async Task CreateEvent_WhenServiceThrowsEventValidationException_ReturnsBadRequest()
        {
            var request = ValidCreateRequest();

            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), It.IsAny<string>()))
                .ThrowsAsync(new EventValidationException("El evento debe tener al menos un tipo de ticket."));

            var response = await _client.PostAsJsonAsync("/api/events", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.InternalServerError)
                throw new Exception("Server returned 500: " + content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task CreateEvent_MissingRequiredFields_ReturnsBadRequest()
        {
            // La ausencia de Ubicacion/TicketGroups la debe atrapar la validación de DataAnnotations
            // del DTO antes de llegar al servicio (docs/api-mvp-plan.md §2). No se verifica la
            // ausencia de invocación al mock: MockEventService se comparte entre todos los tests de
            // esta clase (IClassFixture), así que sus invocaciones se acumulan entre tests.
            var request = new EventCreateRequest { Nombre = "Festival Roto", FechaInicio = DateTime.UtcNow.AddDays(5) };

            var response = await _client.PostAsJsonAsync("/api/events", request);

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
        public async Task PublishEvent_InvalidTransition_ReturnsConflict()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("event-ya-publicado", "test-uid-123"))
                .ThrowsAsync(new EventInvalidTransitionException("event-ya-publicado", "Publicado", "Publicado"));

            var response = await _client.PostAsync("/api/events/event-ya-publicado/publish", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task PublishEvent_MissingTicketTypes_ReturnsConflict()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("event-sin-tickets", "test-uid-123"))
                .ThrowsAsync(new EventMissingTicketTypesException("event-sin-tickets"));

            var response = await _client.PostAsync("/api/events/event-sin-tickets/publish", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
        public async Task CancelEvent_InvalidTransition_ReturnsConflict()
        {
            // Cubre tanto doble cancelación como cancelar un evento ya Finalizado: ambos casos
            // se resuelven en el servicio con la misma excepción tipada (docs/api-mvp-plan.md §1).
            _factory.MockEventService
                .Setup(s => s.CancelEventAsync("event-terminal", "test-uid-123"))
                .ThrowsAsync(new EventInvalidTransitionException("event-terminal", "Cancelado", "Cancelado"));

            var response = await _client.PostAsync("/api/events/event-terminal/cancel", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_OwnEvent_ReturnsOk()
        {
            var request = ValidUpdateRequest();

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-own", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ReturnsAsync(new EventResponse { Id = "event-own", Nombre = request.Nombre });

            var response = await _client.PutAsJsonAsync("/api/events/event-own", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_ForeignEvent_ReturnsForbidden()
        {
            var request = ValidUpdateRequest();

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-foreign", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ThrowsAsync(new EventOwnershipException("event-foreign", "test-uid-123"));

            var response = await _client.PutAsJsonAsync("/api/events/event-foreign", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_NotEditable_ReturnsConflict()
        {
            var request = ValidUpdateRequest();

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-publicado", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ThrowsAsync(new EventNotEditableException("event-publicado"));

            var response = await _client.PutAsJsonAsync("/api/events/event-publicado", request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_WhenServiceThrowsEventValidationException_ReturnsBadRequest()
        {
            var request = ValidUpdateRequest();

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-own", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ThrowsAsync(new EventValidationException("El precio no puede ser negativo."));

            var response = await _client.PutAsJsonAsync("/api/events/event-own", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---- IdentityNotProvisionedException: 403 genérico, nunca 400, nunca el UID en el body ----

        [Fact]
        public async Task CreateEvent_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), "test-uid-123"))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var response = await _client.PostAsJsonAsync("/api/events", ValidCreateRequest());
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
            var msg = RequestSinAccion(HttpMethod.Post, "/api/events");
            msg.Content = JsonContent.Create(ValidCreateRequest());

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
            var msg = RequestSinAccion(HttpMethod.Put, "/api/events/event-1");
            msg.Content = JsonContent.Create(ValidUpdateRequest());

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetMyEvents_SinAccionEventoVerPropios_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Get, "/api/events/organizer/me"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetOwnedEvent_SinAccionEventoVerPropios_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Get, "/api/events/organizer/event-1"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- Etapa 5: los endpoints públicos siguen siendo anónimos ----

        [Fact]
        public async Task GetEvent_Anonymous_ReturnsOkOrNotFound_NeverUnauthorized()
        {
            _factory.MockEventService.Setup(s => s.GetByIdAsync("event-publico")).ReturnsAsync((EventResponse?)null);

            var anonClient = _factory.CreateClient(); // sin Authorization header
            var response = await anonClient.GetAsync("/api/events/event-publico");

            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetEvent_Anonymous_NullResult_ReturnsNotFound()
        {
            _factory.MockEventService.Setup(s => s.GetByIdAsync("event-no-visible")).ReturnsAsync((EventResponse?)null);

            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync("/api/events/event-no-visible");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetEvent_Anonymous_PublicadoVigente_ReturnsOkWithEventResponse()
        {
            var expected = new EventResponse
            {
                Id = "event-publico",
                Nombre = "Evento publico",
                Estado = Event.EventEffectiveStatus.Publicado
            };
            _factory.MockEventService.Setup(s => s.GetByIdAsync("event-publico")).ReturnsAsync(expected);

            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync("/api/events/event-publico");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<EventResponse>();
            Assert.NotNull(result);
            Assert.Equal(Event.EventEffectiveStatus.Publicado, result!.Estado);
        }

        [Fact]
        public async Task SearchEvents_Anonymous_ReturnsOk()
        {
            _factory.MockEventService
                .Setup(s => s.SearchEventsAsync(It.IsAny<EventSearchFilterDto>()))
                .ReturnsAsync(new PagedResponse<EventResponse> { Data = new List<EventResponse>(), HasNextPage = false });

            var anonClient = _factory.CreateClient(); // sin Authorization header
            var response = await anonClient.GetAsync("/api/events");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ---- GET /api/events/organizer/{id}: detalle propio en cualquier estado ----

        [Fact]
        public async Task GetOwnedEvent_OwnEvent_ReturnsOk()
        {
            var expected = new EventResponse
            {
                Id = "event-propio-borrador",
                Nombre = "Evento propio",
                Estado = Event.EventEffectiveStatus.Borrador
            };

            _factory.MockEventService
                .Setup(s => s.GetOwnedByIdAsync("event-propio-borrador", "test-uid-123"))
                .ReturnsAsync(expected);

            var response = await _client.GetAsync("/api/events/organizer/event-propio-borrador");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<EventResponse>();
            Assert.NotNull(result);
            Assert.Equal(Event.EventEffectiveStatus.Borrador, result!.Estado);
        }

        [Fact]
        public async Task GetOwnedEvent_ForeignEvent_ReturnsForbidden()
        {
            _factory.MockEventService
                .Setup(s => s.GetOwnedByIdAsync("event-ajeno", "test-uid-123"))
                .ThrowsAsync(new EventOwnershipException("event-ajeno", "test-uid-123"));

            var response = await _client.GetAsync("/api/events/organizer/event-ajeno");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetOwnedEvent_Nonexistent_ReturnsNotFound()
        {
            _factory.MockEventService
                .Setup(s => s.GetOwnedByIdAsync("event-inexistente", "test-uid-123"))
                .ThrowsAsync(new EventNotFoundException("event-inexistente"));

            var response = await _client.GetAsync("/api/events/organizer/event-inexistente");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
