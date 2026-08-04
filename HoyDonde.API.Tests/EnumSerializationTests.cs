using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Ajuste de contrato backend<->frontend (post API-MVP 4): los enums HTTP de Event/Ticket
    // (EventEffectiveStatus/EventCategory en EventResponse/EventCreateRequest/EventUpdateRequest)
    // viajan siempre como el NOMBRE del miembro (p. ej. "Publicado", "Musica"), nunca como su
    // valor entero subyacente. Configurado globalmente en Program.cs vía JsonStringEnumConverter
    // (allowIntegerValues: false): un entero o un nombre inválido en el body de un request se
    // rechaza en la deserialización, lo que ASP.NET Core traduce automáticamente en un
    // ModelState inválido -> el mismo contrato uniforme de error (VALIDATION_ERROR) ya existente,
    // sin ningún cambio en los controllers.
    public class EnumSerializationTests : IClassFixture<TestApplicationFactory>
    {
        private readonly TestApplicationFactory _factory;
        private readonly HttpClient _client;

        public EnumSerializationTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            _factory.GrantAccion("test-uid-123", "usuario-enum-test", "persona-enum-test",
                Acciones.EventoCrear, Acciones.TicketComprar);
        }

        private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

        private static string BuildEventJson(string categoriaJsonLiteral)
        {
            var fechaInicio = DateTime.UtcNow.AddDays(5).ToString("o");
            var fechaFin = DateTime.UtcNow.AddDays(6).ToString("o");
            return "{"
                + "\"nombre\":\"Festival\","
                + "\"descripcion\":\"desc\","
                + $"\"fechaInicio\":\"{fechaInicio}\","
                + $"\"fechaFin\":\"{fechaFin}\","
                + "\"ubicacion\":\"La Plaza\","
                + $"\"categoria\":{categoriaJsonLiteral},"
                + "\"ticketGroups\":[{\"nombre\":\"General\",\"precio\":10,\"cantidadDisponible\":5}]"
                + "}";
        }

        // ---- Response: EventResponse expone Estado/Categoria como string, nunca como número ----

        [Fact]
        public async Task GetEvent_Response_SerializesEstadoAndCategoria_AsStrings_NotNumbers()
        {
            var expected = new EventResponse
            {
                Id = "event-1",
                Nombre = "Festival",
                Categoria = Event.EventCategory.Musica,
                Estado = Event.EventEffectiveStatus.Publicado,
            };
            _factory.MockEventService.Setup(s => s.GetByIdAsync("event-1")).ReturnsAsync(expected);

            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync("/api/events/event-1");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(content);
            Assert.Equal("Publicado", doc.RootElement.GetProperty("estado").GetString());
            Assert.Equal("Musica", doc.RootElement.GetProperty("categoria").GetString());
        }

        [Fact]
        public async Task GetEvent_Response_NeverSerializesEstadoOrCategoria_AsNumber()
        {
            var expected = new EventResponse
            {
                Id = "event-2",
                Nombre = "Festival Cancelado",
                Categoria = Event.EventCategory.Deportes,
                Estado = Event.EventEffectiveStatus.Cancelado,
            };
            _factory.MockEventService.Setup(s => s.GetByIdAsync("event-2")).ReturnsAsync(expected);

            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync("/api/events/event-2");
            var content = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(content);
            Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("estado").ValueKind);
            Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("categoria").ValueKind);
        }

        // ---- Response: TicketResponseDto.Estado ya es un string manual (no enum-typed en el
        // DTO); sigue viajando como string sin depender del converter global (regresión). ----

        [Fact]
        public async Task BuyTickets_Response_SerializesEstado_AsString()
        {
            var compra = new CompraResponseDto
            {
                Id = "compra-1",
                EventoId = "event-1",
                Tickets = new List<TicketResponseDto>
                {
                    new() { Id = "ticket-1", CompraId = "compra-1", EventoId = "event-1", TicketTypeId = "tipo-1", Estado = "Emitido", Utilizable = true },
                },
            };
            _factory.MockTicketService
                .Setup(s => s.BuyTicketsAsync("test-uid-123", It.IsAny<TicketBuyRequest>()))
                .ReturnsAsync(compra);

            var request = new TicketBuyRequest { EventoId = "event-1", TicketTypeId = "tipo-1", Cantidad = 1 };
            var response = await _client.PostAsJsonAsync("/api/tickets/buy", request);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(content);
            Assert.Equal("Emitido", doc.RootElement.GetProperty("tickets")[0].GetProperty("estado").GetString());
        }

        // ---- Request: un nombre válido de Categoria se acepta ----

        [Fact]
        public async Task CreateEvent_CategoriaAsValidStringName_IsAccepted()
        {
            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), "test-uid-123"))
                .ReturnsAsync(new EventResponse { Id = "e1", Estado = Event.EventEffectiveStatus.Borrador });

            var response = await _client.PostAsync("/api/events", JsonBody(BuildEventJson("\"Musica\"")));
            var content = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Esperaba 200, fue {response.StatusCode}: {content}");
        }

        // ---- Request: un número entero para Categoria se rechaza (allowIntegerValues:false),
        // mediante el mismo contrato uniforme de error ya existente ----

        [Fact]
        public async Task CreateEvent_CategoriaAsInteger_IsRejected_WithValidationErrorContract()
        {
            // No se verifica ausencia de invocación al mock: MockEventService se comparte entre
            // todos los tests de esta clase (IClassFixture), igual que en EventsControllerTests.
            var response = await _client.PostAsync("/api/events", JsonBody(BuildEventJson("0")));
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var doc = JsonDocument.Parse(content);
            Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("code").GetString());
            Assert.True(doc.RootElement.TryGetProperty("traceId", out var traceId));
            Assert.False(string.IsNullOrEmpty(traceId.GetString()));
        }

        // ---- Request: un nombre inválido de Categoria se rechaza, mismo contrato ----

        [Fact]
        public async Task CreateEvent_CategoriaAsInvalidName_IsRejected_WithValidationErrorContract()
        {
            var response = await _client.PostAsync("/api/events", JsonBody(BuildEventJson("\"NoExisteEstaCategoria\"")));
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var doc = JsonDocument.Parse(content);
            Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("code").GetString());
        }
    }
}
