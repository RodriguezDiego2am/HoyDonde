using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    // GET /api/reports/organizer/events (docs/api-mvp-plan.md §11): contrato HTTP/policy contra
    // MockReporteService (mismo patrón que EventsControllerTests). La lógica real de
    // validación/agregación se cubre en ReporteFiltroValidatorTests/ReporteMetricasCalculatorTests
    // (unitarios) e Integration/ReporteServiceEmulatorTests (Firestore Emulator).
    public class ReportsControllerTests : IClassFixture<TestApplicationFactory>
    {
        private readonly TestApplicationFactory _factory;
        private readonly HttpClient _client;

        public ReportsControllerTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            _factory.GrantAccion("test-uid-123", "usuario-reports-test", "persona-reports-test", Acciones.ReporteVerPropio);
        }

        private static string ValidQuery(string? extra = null) =>
            "/api/reports/organizer/events?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z" + (extra ?? "");

        // ---- 401: sin autenticación ----

        [Fact]
        public async Task GetOrganizerEventsReport_Anonymous_ReturnsUnauthorized()
        {
            var anonClient = _factory.CreateClient();

            var response = await anonClient.GetAsync(ValidQuery());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ---- 403: sin la acción REPORTE_VER_PROPIO ----

        [Fact]
        public async Task GetOrganizerEventsReport_SinAccionReporteVerPropio_ReturnsForbidden()
        {
            var msg = new HttpRequestMessage(HttpMethod.Get, ValidQuery());
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-reports");

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- Con la acción: contrato correcto ----

        [Fact]
        public async Task GetOrganizerEventsReport_ConAccion_ReturnsOk_WithExpectedShape()
        {
            var expected = new ReporteEventosResponseDto
            {
                FechaDesde = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FechaHasta = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                AclaracionImporte = "El MVP no procesa pagos reales.",
                Resumen = new ReporteResumenDto { CantidadEventos = 1, EntradasEmitidas = 2 },
                Eventos = new List<ReporteEventoDetalleDto>
                {
                    new() { EventId = "event-1", Nombre = "Festival", EntradasEmitidas = 2 },
                },
            };

            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync("test-uid-123", It.IsAny<ReporteEventosFilterDto>()))
                .ReturnsAsync(expected);

            var response = await _client.GetAsync(ValidQuery());
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ReporteEventosResponseDto>();
            Assert.NotNull(result);
            Assert.Equal(1, result!.Resumen.CantidadEventos);
            Assert.Single(result.Eventos);
            Assert.Equal("event-1", result.Eventos[0].EventId);
            // Nunca expone identificadores internos del actor autenticado.
            Assert.DoesNotContain("test-uid-123", content);
            Assert.DoesNotContain("persona-reports-test", content);
        }

        // ---- Model binding: fechas/enums ----

        [Fact]
        public async Task GetOrganizerEventsReport_BindsFechasEstadoCategoriaEventIdTicketTypeId()
        {
            ReporteEventosFilterDto? captured = null;
            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync(It.IsAny<string>(), It.IsAny<ReporteEventosFilterDto>()))
                .Callback<string, ReporteEventosFilterDto>((_, f) => captured = f)
                .ReturnsAsync(new ReporteEventosResponseDto());

            var response = await _client.GetAsync(
                "/api/reports/organizer/events?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z&estado=Publicado&categoria=Musica&eventId=event-1&ticketTypeId=tipo-1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(captured);
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), captured!.FechaDesde);
            Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), captured.FechaHasta);
            Assert.Equal(Event.EventEffectiveStatus.Publicado, captured.Estado);
            Assert.Equal(Event.EventCategory.Musica, captured.Categoria);
            Assert.Equal("event-1", captured.EventId);
            Assert.Equal("tipo-1", captured.TicketTypeId);
        }

        [Fact]
        public async Task GetOrganizerEventsReport_EstadoInexistente_ReturnsValidationError400()
        {
            var response = await _client.GetAsync(ValidQuery("&estado=NoExiste"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task GetOrganizerEventsReport_CategoriaInexistente_ReturnsValidationError400()
        {
            var response = await _client.GetAsync(ValidQuery("&categoria=NoExiste"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---- Errores tipados propagados con el contrato uniforme ----

        [Fact]
        public async Task GetOrganizerEventsReport_RangoInvalido_ReturnsBadRequest_WithReportRangeInvalidCode()
        {
            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync("test-uid-123", It.IsAny<ReporteEventosFilterDto>()))
                .ThrowsAsync(new ReporteRangoInvalidoException("El rango no puede exceder 366 días."));

            var response = await _client.GetAsync(ValidQuery());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REPORT_RANGE_INVALID", body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task GetOrganizerEventsReport_FiltroInvalido_ReturnsBadRequest_WithReportFilterInvalidCode()
        {
            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync("test-uid-123", It.IsAny<ReporteEventosFilterDto>()))
                .ThrowsAsync(new ReporteFiltroInvalidoException("'ticketTypeId' requiere 'eventId'."));

            var response = await _client.GetAsync(ValidQuery());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REPORT_FILTER_INVALID", body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task GetOrganizerEventsReport_EventoAjeno_ReturnsForbidden()
        {
            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync("test-uid-123", It.IsAny<ReporteEventosFilterDto>()))
                .ThrowsAsync(new EventOwnershipException("event-ajeno", "test-uid-123"));

            var response = await _client.GetAsync(ValidQuery("&eventId=event-ajeno"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetOrganizerEventsReport_EventoInexistente_ReturnsNotFound()
        {
            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync("test-uid-123", It.IsAny<ReporteEventosFilterDto>()))
                .ThrowsAsync(new EventNotFoundException("event-inexistente"));

            var response = await _client.GetAsync(ValidQuery("&eventId=event-inexistente"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetOrganizerEventsReport_TicketTypeInvalido_ReturnsNotFound()
        {
            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync("test-uid-123", It.IsAny<ReporteEventosFilterDto>()))
                .ThrowsAsync(new TicketTypeInvalidoException("event-1", "tipo-ajeno"));

            var response = await _client.GetAsync(ValidQuery("&eventId=event-1&ticketTypeId=tipo-ajeno"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetOrganizerEventsReport_ActorNotProvisioned_ReturnsForbidden_WithoutLeakingUid()
        {
            _factory.MockReporteService
                .Setup(s => s.GetOrganizerEventsReportAsync("test-uid-123", It.IsAny<ReporteEventosFilterDto>()))
                .ThrowsAsync(new IdentityNotProvisionedException("test-uid-123"));

            var response = await _client.GetAsync(ValidQuery());
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("test-uid-123", content);
        }
    }
}
