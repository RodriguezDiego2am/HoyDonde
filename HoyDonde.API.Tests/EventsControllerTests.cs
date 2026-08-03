using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        // Program.cs configura globalmente JsonStringEnumConverter para que Categoria/Estado
        // viajen como el nombre del enum, nunca como número (ajuste de contrato backend<->
        // frontend). System.Net.Http.Json usa sus propias JsonSerializerOptions por defecto (sin
        // ese converter), así que los tests que serializan/deserializan un EventCreateRequest/
        // EventUpdateRequest/EventResponse necesitan estas mismas options explícitamente para
        // reflejar el contrato HTTP real, en vez de degradar Categoria/Estado a un entero.
        private static readonly JsonSerializerOptions EnumAwareJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        };

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
                Acciones.EventoCancelarPropio, Acciones.EventoVerPropios, Acciones.ControlCrear,
                Acciones.TicketValidar);
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

            var response = await _client.PostAsJsonAsync("/api/events", request, EnumAwareJson);

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception(content);

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EventResponse>(EnumAwareJson);
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

            var response = await _client.PostAsJsonAsync("/api/events", request, EnumAwareJson);
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

            var response = await _client.PutAsJsonAsync("/api/events/event-own", request, EnumAwareJson);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_ForeignEvent_ReturnsForbidden()
        {
            var request = ValidUpdateRequest();

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-foreign", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ThrowsAsync(new EventOwnershipException("event-foreign", "test-uid-123"));

            var response = await _client.PutAsJsonAsync("/api/events/event-foreign", request, EnumAwareJson);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_NotEditable_ReturnsConflict()
        {
            var request = ValidUpdateRequest();

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-publicado", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ThrowsAsync(new EventNotEditableException("event-publicado"));

            var response = await _client.PutAsJsonAsync("/api/events/event-publicado", request, EnumAwareJson);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEvent_WhenServiceThrowsEventValidationException_ReturnsBadRequest()
        {
            var request = ValidUpdateRequest();

            _factory.MockEventService
                .Setup(s => s.UpdateEventAsync("event-own", "test-uid-123", It.IsAny<EventUpdateRequest>()))
                .ThrowsAsync(new EventValidationException("El precio no puede ser negativo."));

            var response = await _client.PutAsJsonAsync("/api/events/event-own", request, EnumAwareJson);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---- IdentityNotProvisionedException: 403 genérico, nunca 400, nunca el UID en el body ----

        [Fact]
        public async Task CreateEvent_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), "test-uid-123"))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var response = await _client.PostAsJsonAsync("/api/events", ValidCreateRequest(), EnumAwareJson);
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
            var result = await response.Content.ReadFromJsonAsync<EventResponse>(EnumAwareJson);
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

        // ---- GET /api/events: contrato de query string de los filtros de Frontend 5 ----

        [Fact]
        public async Task SearchEvents_FechaDesdeHastaCategoriaUbicacion_BindCorrectlyIntoFilterDto()
        {
            EventSearchFilterDto? capturedFilter = null;
            _factory.MockEventService
                .Setup(s => s.SearchEventsAsync(It.IsAny<EventSearchFilterDto>()))
                .Callback<EventSearchFilterDto>(f => capturedFilter = f)
                .ReturnsAsync(new PagedResponse<EventResponse> { Data = new List<EventResponse>(), HasNextPage = false });

            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync(
                "/api/events?fechaDesde=2026-08-05T00:00:00Z&fechaHasta=2026-08-11T00:00:00Z&categoria=Musica&ubicacion=Parque%20Central&lastEventId=cursor-1&limit=5");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(capturedFilter);
            Assert.Equal(new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), capturedFilter!.FechaDesde);
            Assert.Equal(new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc), capturedFilter.FechaHasta);
            Assert.Equal(Event.EventCategory.Musica, capturedFilter.Categoria);
            Assert.Equal("Parque Central", capturedFilter.Ubicacion);
            Assert.Equal("cursor-1", capturedFilter.LastEventId);
            Assert.Equal(5, capturedFilter.Limit);
        }

        [Fact]
        public async Task SearchEvents_CategoriaInexistente_ReturnsValidationError400()
        {
            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync("/api/events?categoria=NoExiste");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task SearchEvents_DesdeMayorQueHasta_PropagatesTypedExceptionAsEventValidationError400()
        {
            _factory.MockEventService
                .Setup(s => s.SearchEventsAsync(It.IsAny<EventSearchFilterDto>()))
                .ThrowsAsync(new EventValidationException("La fecha 'Desde' no puede ser posterior a la fecha 'Hasta'."));

            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync("/api/events?fechaDesde=2026-08-11T00:00:00Z&fechaHasta=2026-08-05T00:00:00Z");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("EVENT_VALIDATION_ERROR", body.GetProperty("code").GetString());
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
            var result = await response.Content.ReadFromJsonAsync<EventResponse>(EnumAwareJson);
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

        // ---- POST /api/events/{eventId}/controls/{controlPersonaId} (API-MVP 3) ----

        [Fact]
        public async Task AssignControl_OwnEvent_ReturnsOk_WithBoundedDto_NeverLeakingUidOrExternalSubjectId()
        {
            var asignacion = new ControlAsignacion
            {
                Id = "control-persona-1_event-2",
                ControlPersonaId = "control-persona-1",
                EventId = "event-2",
                AssignedByPersonaId = "persona-events-test",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "event-2", "control-persona-1"))
                .ReturnsAsync(asignacion);

            var response = await _client.PostAsync("/api/events/event-2/controls/control-persona-1", null);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ControlAsignacionResponseDto>();
            Assert.NotNull(result);
            Assert.Equal("control-persona-1", result!.ControlPersonaId);
            Assert.Equal("event-2", result.EventId);
            Assert.Equal("persona-events-test", result.AssignedByPersonaId);
            Assert.DoesNotContain("test-uid-123", content);
            Assert.DoesNotContain("ExternalSubjectId", content);
            Assert.DoesNotContain("UsuarioId", content);
        }

        [Fact]
        public async Task AssignControl_ForeignEvent_ReturnsForbidden()
        {
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "event-ajeno", "control-persona-1"))
                .ThrowsAsync(new EventOwnershipException("event-ajeno", "test-uid-123"));

            var response = await _client.PostAsync("/api/events/event-ajeno/controls/control-persona-1", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AssignControl_NonexistentEvent_ReturnsNotFound()
        {
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "event-inexistente", "control-persona-1"))
                .ThrowsAsync(new EventNotFoundException("event-inexistente"));

            var response = await _client.PostAsync("/api/events/event-inexistente/controls/control-persona-1", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task AssignControl_ControlInvalido_ReturnsNotFound()
        {
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "event-2", "persona-no-control"))
                .ThrowsAsync(new ControlInvalidoException("persona-no-control"));

            var response = await _client.PostAsync("/api/events/event-2/controls/persona-no-control", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task AssignControl_ControlAjeno_ReturnsForbidden()
        {
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "event-2", "control-de-otro-organizador"))
                .ThrowsAsync(new ControlAjenoException("control-de-otro-organizador"));

            var response = await _client.PostAsync("/api/events/event-2/controls/control-de-otro-organizador", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AssignControl_EventoCanceladoOFinalizado_ReturnsConflict()
        {
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "event-terminal", "control-persona-1"))
                .ThrowsAsync(new EventoNoDisponibleParaAsignacionControlException("event-terminal"));

            var response = await _client.PostAsync("/api/events/event-terminal/controls/control-persona-1", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task AssignControl_SinAccionControlCrear_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Post, "/api/events/event-2/controls/control-persona-1"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AssignControl_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "event-2", "control-persona-1"))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var response = await _client.PostAsync("/api/events/event-2/controls/control-persona-1", null);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("test-uid-123", content);
        }

        // ---- GET /api/events/organizer/controls (API-MVP 5) ----

        [Fact]
        public async Task GetMyControls_ReturnsOk_WithBoundedDto_NeverLeakingUidOrUsuarioId()
        {
            var controles = new List<ControlResumenResponseDto>
            {
                new() { ControlPersonaId = "control-persona-1", UserName = "control-uno", Activo = true },
            };
            _factory.MockUserService
                .Setup(s => s.ListarControlesDelOrganizadorAsync("test-uid-123"))
                .ReturnsAsync(controles);

            var response = await _client.GetAsync("/api/events/organizer/controls");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<List<ControlResumenResponseDto>>();
            Assert.NotNull(result);
            Assert.Single(result!);
            Assert.Equal("control-persona-1", result[0].ControlPersonaId);
            Assert.Equal("control-uno", result[0].UserName);
            Assert.DoesNotContain("test-uid-123", content);
            Assert.DoesNotContain("ExternalSubjectId", content);
            Assert.DoesNotContain("UsuarioId", content);
        }

        [Fact]
        public async Task GetMyControls_ReturnsEmptyList_WhenOrganizerNeverAssignedAnyControl()
        {
            _factory.MockUserService
                .Setup(s => s.ListarControlesDelOrganizadorAsync("test-uid-123"))
                .ReturnsAsync(new List<ControlResumenResponseDto>());

            var response = await _client.GetAsync("/api/events/organizer/controls");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<List<ControlResumenResponseDto>>();
            Assert.NotNull(result);
            Assert.Empty(result!);
        }

        [Fact]
        public async Task GetMyControls_SinAccionControlCrear_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Get, "/api/events/organizer/controls"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- GET /api/events/{eventId}/controls (API-MVP 5) ----

        [Fact]
        public async Task GetEventControls_OwnEvent_ReturnsOk_WithAssignmentMetadata()
        {
            var controles = new List<ControlAsignadoResponseDto>
            {
                new()
                {
                    ControlPersonaId = "control-persona-1",
                    UserName = "control-uno",
                    Activo = true,
                    AssignedByPersonaId = "persona-events-test",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            };
            _factory.MockUserService
                .Setup(s => s.ListarControlesDelEventoAsync("test-uid-123", "event-2"))
                .ReturnsAsync(controles);

            var response = await _client.GetAsync("/api/events/event-2/controls");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<List<ControlAsignadoResponseDto>>();
            Assert.NotNull(result);
            Assert.Single(result!);
            Assert.Equal("control-persona-1", result[0].ControlPersonaId);
            Assert.Equal("persona-events-test", result[0].AssignedByPersonaId);
        }

        [Fact]
        public async Task GetEventControls_EventWithNoControls_ReturnsEmptyList()
        {
            _factory.MockUserService
                .Setup(s => s.ListarControlesDelEventoAsync("test-uid-123", "event-2"))
                .ReturnsAsync(new List<ControlAsignadoResponseDto>());

            var response = await _client.GetAsync("/api/events/event-2/controls");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<List<ControlAsignadoResponseDto>>();
            Assert.NotNull(result);
            Assert.Empty(result!);
        }

        [Fact]
        public async Task GetEventControls_ForeignEvent_ReturnsForbidden()
        {
            _factory.MockUserService
                .Setup(s => s.ListarControlesDelEventoAsync("test-uid-123", "event-ajeno"))
                .ThrowsAsync(new EventOwnershipException("event-ajeno", "test-uid-123"));

            var response = await _client.GetAsync("/api/events/event-ajeno/controls");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetEventControls_NonexistentEvent_ReturnsNotFound()
        {
            _factory.MockUserService
                .Setup(s => s.ListarControlesDelEventoAsync("test-uid-123", "event-inexistente"))
                .ThrowsAsync(new EventNotFoundException("event-inexistente"));

            var response = await _client.GetAsync("/api/events/event-inexistente/controls");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetEventControls_SinAccionControlCrear_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Get, "/api/events/event-2/controls"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- GET /api/events/control/me (API-MVP 5) ----

        [Fact]
        public async Task GetMyAssignedEvents_ReturnsOk_WithoutTicketTypesOrPrices()
        {
            var eventos = new List<EventoAsignadoResponseDto>
            {
                new()
                {
                    EventId = "event-1",
                    Nombre = "Festival",
                    Ubicacion = "La Plaza",
                    FechaInicio = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    FechaFin = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    Estado = Event.EventEffectiveStatus.Publicado,
                },
            };
            _factory.MockUserService
                .Setup(s => s.ListarEventosAsignadosAsync("test-uid-123"))
                .ReturnsAsync(eventos);

            var response = await _client.GetAsync("/api/events/control/me");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<List<EventoAsignadoResponseDto>>(EnumAwareJson);
            Assert.NotNull(result);
            Assert.Single(result!);
            Assert.Equal("event-1", result[0].EventId);
            Assert.Equal(Event.EventEffectiveStatus.Publicado, result[0].Estado);
            Assert.DoesNotContain("TicketGroups", content);
            Assert.DoesNotContain("Precio", content);
            Assert.DoesNotContain("CantidadDisponible", content);
        }

        [Fact]
        public async Task GetMyAssignedEvents_ReturnsEmptyList_WhenControlHasNoAsignaciones()
        {
            _factory.MockUserService
                .Setup(s => s.ListarEventosAsignadosAsync("test-uid-123"))
                .ReturnsAsync(new List<EventoAsignadoResponseDto>());

            var response = await _client.GetAsync("/api/events/control/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<List<EventoAsignadoResponseDto>>();
            Assert.NotNull(result);
            Assert.Empty(result!);
        }

        [Fact]
        public async Task GetMyAssignedEvents_SinAccionTicketValidar_ReturnsForbidden()
        {
            var response = await _client.SendAsync(RequestSinAccion(HttpMethod.Get, "/api/events/control/me"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
