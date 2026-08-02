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
using HoyDonde.API.Middleware;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using HoyDonde.API.Tests.Integration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // API-MVP 4 (docs/api-mvp-plan.md §5): contrato uniforme de error. Verifica el Code estable
    // de un representante de cada familia de excepción tipada, el contrato de ModelState, y que
    // un 500 nunca filtra detalles internos en Production (a diferencia de Development, donde el
    // campo Detail es una ayuda de diagnóstico deliberada).
    public class ErrorContractTests : IClassFixture<TestApplicationFactory>
    {
        private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        // Program.cs configura globalmente JsonStringEnumConverter (Categoria/Estado viajan como
        // el nombre del enum, nunca como número); System.Net.Http.Json usa sus propias
        // JsonSerializerOptions por defecto (sin ese converter), así que un test que serializa un
        // EventCreateRequest necesita estas mismas options para no enviar un entero de vuelta.
        private static readonly JsonSerializerOptions EnumAwareJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        };

        private readonly TestApplicationFactory _factory;
        private readonly HttpClient _client;

        public ErrorContractTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            _factory.GrantAccion("test-uid-123", "usuario-error-contract-test", "persona-error-contract-test",
                Acciones.EventoCrear, Acciones.EventoPublicarPropio, Acciones.TicketComprar,
                Acciones.ControlCrear, Acciones.UsuarioCrearAdmin);
        }

        private static async Task<ErrorResponse> ReadErrorAsync(HttpResponseMessage response)
        {
            var result = await response.Content.ReadFromJsonAsync<ErrorResponse>(CaseInsensitive);
            Assert.NotNull(result);
            return result!;
        }

        // ---- Un representante de cada familia de status/Code ----

        [Fact]
        public async Task EventNotFound_Returns404_WithStableCode()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("evento-inexistente", "test-uid-123"))
                .ThrowsAsync(new EventNotFoundException("evento-inexistente"));

            var response = await _client.PostAsync("/api/events/evento-inexistente/publish", null);
            var error = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("EVENT_NOT_FOUND", error.Code);
            Assert.NotEmpty(error.TraceId);
        }

        [Fact]
        public async Task EventOwnership_Returns403_WithStableCode_AndNeverLeaksActorUid()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("evento-ajeno", "test-uid-123"))
                .ThrowsAsync(new EventOwnershipException("evento-ajeno", "test-uid-123"));

            var response = await _client.PostAsync("/api/events/evento-ajeno/publish", null);
            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ErrorResponse>(content, CaseInsensitive)!;

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("EVENT_OWNERSHIP", error.Code);
            Assert.DoesNotContain("test-uid-123", content);
        }

        [Fact]
        public async Task EventInvalidTransition_Returns409_WithStableCode()
        {
            _factory.MockEventService
                .Setup(s => s.PublishEventAsync("evento-terminal", "test-uid-123"))
                .ThrowsAsync(new EventInvalidTransitionException("evento-terminal", "Cancelado", "Publicado"));

            var response = await _client.PostAsync("/api/events/evento-terminal/publish", null);
            var error = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("EVENT_INVALID_TRANSITION", error.Code);
        }

        [Fact]
        public async Task StockInsuficiente_Returns409_WithStableCode()
        {
            var request = new TicketBuyRequest { EventoId = "evento-1", TicketTypeId = "tipo-1", Cantidad = 5 };
            _factory.MockTicketService
                .Setup(s => s.BuyTicketsAsync("test-uid-123", It.IsAny<TicketBuyRequest>()))
                .ThrowsAsync(new StockInsuficienteException("tipo-1", 2, 5));

            var response = await _client.PostAsJsonAsync("/api/tickets/buy", request);
            var error = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("TICKET_STOCK_INSUFFICIENT", error.Code);
        }

        [Fact]
        public async Task ControlInvalido_Returns404_WithStableCode()
        {
            _factory.MockUserService
                .Setup(s => s.AsignarControlExistenteAsync("test-uid-123", "evento-1", "persona-no-control"))
                .ThrowsAsync(new ControlInvalidoException("persona-no-control"));

            var response = await _client.PostAsync("/api/events/evento-1/controls/persona-no-control", null);
            var error = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("CONTROL_INVALID", error.Code);
        }

        [Fact]
        public async Task IdentityEmailAlreadyExists_Returns409_WithStableCode()
        {
            var request = new RegisterAdminDto { Email = "duplicado@test.com", Password = "Password123!" };
            _factory.MockUserService
                .Setup(s => s.RegisterAdminAsync("test-uid-123", request.Email, request.Password))
                .ThrowsAsync(new IdentityEmailAlreadyExistsException(request.Email));

            var response = await _client.PostAsJsonAsync("/api/users/admin", request);
            var error = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("IDENTITY_EMAIL_ALREADY_EXISTS", error.Code);
        }

        [Fact]
        public async Task IdentityNotProvisioned_Returns403_WithStableCode_AndGenericMessage()
        {
            _factory.MockEventService
                .Setup(s => s.CreateEventAsync(It.IsAny<EventCreateRequest>(), "test-uid-123"))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var request = new EventCreateRequest
            {
                Nombre = "x",
                Ubicacion = "x",
                FechaInicio = DateTime.UtcNow.AddDays(1),
                FechaFin = DateTime.UtcNow.AddDays(2),
                TicketGroups = new List<TicketGroupDto> { new() { Nombre = "General", Precio = 1, CantidadDisponible = 1 } }
            };

            var response = await _client.PostAsJsonAsync("/api/events", request, EnumAwareJson);
            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ErrorResponse>(content, CaseInsensitive)!;

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("IDENTITY_NOT_PROVISIONED", error.Code);
            Assert.DoesNotContain("test-uid-123", content);
        }

        [Fact]
        public async Task UnexpectedException_InDevelopment_Returns500_WithDiagnosticDetail()
        {
            // A diferencia de Production (ProductionErrorContractTests), en Development el campo
            // Detail sí se completa a propósito, como ayuda de diagnóstico local (docs/api-mvp-plan.md
            // §5: "detalles técnicos solamente en logs y Development").
            _factory.MockEventService
                .Setup(s => s.SearchEventsAsync(It.IsAny<EventSearchFilterDto>()))
                .ThrowsAsync(new InvalidOperationException("fallo interno simulado"));

            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync("/api/events");
            var error = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("UNEXPECTED_ERROR", error.Code);
            Assert.False(string.IsNullOrEmpty(error.Detail));
        }

        // ---- ModelState (DataAnnotations) sigue el mismo contrato ----

        [Fact]
        public async Task InvalidModelState_Returns400_WithValidationErrorCode_AndFieldErrors()
        {
            // Falta Ubicacion y TicketGroups (ambos requeridos por EventCreateRequest).
            var request = new { Nombre = "Festival Roto", FechaInicio = DateTime.UtcNow.AddDays(5) };

            var response = await _client.PostAsJsonAsync("/api/events", request);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var doc = JsonDocument.Parse(content);
            Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("code").GetString());
            Assert.True(doc.RootElement.TryGetProperty("traceId", out var traceId));
            Assert.False(string.IsNullOrEmpty(traceId.GetString()));
            Assert.True(doc.RootElement.TryGetProperty("errors", out var errors));
            Assert.True(errors.EnumerateObject().MoveNext(), "Se esperaba al menos un error de campo.");
        }

        [Fact]
        public async Task RegisterAdmin_WeakPassword_ReturnsValidationError()
        {
            var request = new RegisterAdminDto { Email = "nuevo-admin@test.com", Password = "abc" };

            var response = await _client.PostAsJsonAsync("/api/users/admin", request);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var doc = JsonDocument.Parse(content);
            Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("code").GetString());
        }

        [Fact]
        public async Task RegisterAdmin_InvalidEmail_ReturnsValidationError()
        {
            var request = new RegisterAdminDto { Email = "no-es-un-email", Password = "Password123!" };

            var response = await _client.PostAsJsonAsync("/api/users/admin", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    // 500 genérico en Production, sin exponer nunca el mensaje/tipo real de la excepción
    // (docs/api-mvp-plan.md §5). A diferencia de TestApplicationFactory (siempre Development),
    // esta variante fuerza el entorno a Production; el resto del cableado (mocks) es idéntico.
    public class ProductionErrorContractFactory : WebApplicationFactory<Program>
    {
        public Mock<IEventService> MockEventService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            Environment.SetEnvironmentVariable("Firebase__ProjectId", "test-project-123");

            builder.ConfigureServices(services =>
            {
                // Última registración gana en la resolución no enumerable de DI: no hace falta
                // remover el registro real de Program.cs. ValidateOnBuild solo se activa en
                // Development, así que el resto del grafo (FirestoreDb, repos reales, etc.) puede
                // quedar sin resolver sin que falle el arranque.
                services.AddSingleton(MockEventService.Object);
                services.AddAuthentication("Test")
                        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, FakeAuthHandler>("Test", options => { });
            });
        }
    }

    public class ProductionErrorContractTests : IClassFixture<ProductionErrorContractFactory>
    {
        private readonly ProductionErrorContractFactory _factory;
        private readonly HttpClient _client;

        public ProductionErrorContractTests(ProductionErrorContractFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task UnexpectedException_InProduction_Returns500_WithGenericMessage_AndNoInternalDetails()
        {
            const string secretDetail = "Firestore connection string: postgres://internal-secret-host/db";
            _factory.MockEventService
                .Setup(s => s.SearchEventsAsync(It.IsAny<EventSearchFilterDto>()))
                .ThrowsAsync(new InvalidOperationException(secretDetail));

            var response = await _client.GetAsync("/api/events");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain(secretDetail, content);
            Assert.DoesNotContain("InvalidOperationException", content);
            Assert.DoesNotContain("System.", content);

            using var doc = JsonDocument.Parse(content);
            Assert.Equal("UNEXPECTED_ERROR", doc.RootElement.GetProperty("code").GetString());
            Assert.False(doc.RootElement.TryGetProperty("detail", out var detail) && !string.IsNullOrEmpty(detail.GetString()),
                "Production no debe incluir el campo Detail.");
        }
    }
}
