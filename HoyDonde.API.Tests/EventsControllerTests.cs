using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
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
    }
}
