using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
    public class TicketsControllerTests : IClassFixture<TestApplicationFactory>
    {
        private readonly TestApplicationFactory _factory;
        private readonly System.Net.Http.HttpClient _client;

        public TicketsControllerTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            // Habilitamos el Fake Auth (actualmente "Organizador", así que para testear rol "Cliente", ajustamos el Handler o usamos Mocks de Auth)
            // Por simplicidad, agregaremos el header default
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

            // Etapa 5 del refactor de seguridad: la autorización real depende de IPermissionService,
            // no del claim de rol legacy (Test-Role queda como vestigio inofensivo en estos tests).
            _factory.GrantAccion("test-uid-123", "usuario-tickets-test", "persona-tickets-test",
                Acciones.TicketComprar, Acciones.TicketVerPropio, Acciones.TicketValidar);
        }

        [Fact]
        public async Task BuyTickets_ValidRequest_CallsServiceAndReturnsOk()
        {
            // Arrange
            var request = new TicketBuyRequest
            {
                EventoId = "evento-id-123",
                TicketTypeId = "ticket-tipo-vip",
                Cantidad = 2
            };

            var expectedResponse = new List<TicketResponseDto>
            {
                new TicketResponseDto { Id = "ticket-1", EventoId = "evento-id-123", TicketTypeId = "ticket-tipo-vip" },
                new TicketResponseDto { Id = "ticket-2", EventoId = "evento-id-123", TicketTypeId = "ticket-tipo-vip" }
            };

            _factory.MockTicketService
                .Setup(ts => ts.BuyTicketsAsync("test-uid-123", It.IsNotNull<TicketBuyRequest>()))
                .ReturnsAsync(expectedResponse);

            // Act
            // El TestAuthFakeHandler ahora lee Test-Role para simular roles. Mandamos Cliente
            var reqMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/tickets/buy")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Add("Test-Role", Models.Roles.Cliente);

            var response = await _client.SendAsync(reqMessage);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task BuyTickets_InvalidModel_ReturnsBadRequest()
        {
            // Arrange
            var request = new TicketBuyRequest
            {
                EventoId = "", // Invalid
                TicketTypeId = "ticket-tipo-vip",
                Cantidad = 0 // Invalid
            };

            // Act
            var reqMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/tickets/buy")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Add("Test-Role", Models.Roles.Cliente);

            var response = await _client.SendAsync(reqMessage);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---- ValidateTicket: rol, evento asignado y estado del ticket ----

        private HttpRequestMessage ValidateRequest(string ticketId, string eventId, string role)
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/validate?ticketId={ticketId}&eventId={eventId}");
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Role", role);
            return msg;
        }

        [Fact]
        public async Task ValidateTicket_AssignedEvent_ReturnsOk()
        {
            _factory.MockTicketService
                .Setup(s => s.ValidateTicketAsync("test-uid-123", "ticket-1", "event-1"))
                .ReturnsAsync(TicketValidationOutcome.Success);

            var response = await _client.SendAsync(ValidateRequest("ticket-1", "event-1", Models.Roles.Control));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ValidateTicket_OtherEvent_ReturnsForbidden()
        {
            _factory.MockTicketService
                .Setup(s => s.ValidateTicketAsync("test-uid-123", "ticket-1", "event-2"))
                .ReturnsAsync(TicketValidationOutcome.NotAuthorized);

            var response = await _client.SendAsync(ValidateRequest("ticket-1", "event-2", Models.Roles.Control));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task ValidateTicket_AlreadyUsed_ReturnsConflict()
        {
            _factory.MockTicketService
                .Setup(s => s.ValidateTicketAsync("test-uid-123", "ticket-used", "event-1"))
                .ReturnsAsync(TicketValidationOutcome.AlreadyUsed);

            var response = await _client.SendAsync(ValidateRequest("ticket-used", "event-1", Models.Roles.Control));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task ValidateTicket_Cancelled_ReturnsConflict()
        {
            _factory.MockTicketService
                .Setup(s => s.ValidateTicketAsync("test-uid-123", "ticket-anulado", "event-1"))
                .ReturnsAsync(TicketValidationOutcome.Cancelled);

            var response = await _client.SendAsync(ValidateRequest("ticket-anulado", "event-1", Models.Roles.Control));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task ValidateTicket_SinAccionTicketValidar_ReturnsForbidden()
        {
            // El servicio ni siquiera debería llamarse: la policy TICKET_VALIDAR corta antes.
            var msg = ValidateRequest("ticket-1", "event-1", Models.Roles.Control);
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-tickets");

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task BuyTickets_SinAccionTicketComprar_ReturnsForbidden()
        {
            var request = new TicketBuyRequest { EventoId = "evento-id-123", TicketTypeId = "ticket-tipo-vip", Cantidad = 2 };
            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/buy") { Content = JsonContent.Create(request) };
            reqMessage.Headers.Add("Test-Uid", "uid-sin-permiso-tickets");

            var response = await _client.SendAsync(reqMessage);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task BuyTickets_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            _factory.MockTicketService
                .Setup(ts => ts.BuyTicketsAsync("test-uid-123", It.IsNotNull<TicketBuyRequest>()))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var request = new TicketBuyRequest
            {
                EventoId = "evento-id-123",
                TicketTypeId = "ticket-tipo-vip",
                Cantidad = 2
            };

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/buy")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Add("Test-Role", Models.Roles.Cliente);

            var response = await _client.SendAsync(reqMessage);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("test-uid-123", content);
        }
    }
}
