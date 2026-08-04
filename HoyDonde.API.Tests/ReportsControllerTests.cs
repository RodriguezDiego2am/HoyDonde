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

            _factory.GrantAccion("test-uid-123", "usuario-reports-test", "persona-reports-test", Acciones.ReporteVerPropio, Acciones.ReporteVerGlobal);
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

        // ==================================================================
        // GET /api/reports/admin/events (docs/api-mvp-plan.md §11.3)
        // ==================================================================

        private static string ValidAdminQuery(string? extra = null) =>
            "/api/reports/admin/events?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z" + (extra ?? "");

        [Fact]
        public async Task GetAdminEventsReport_Anonymous_ReturnsUnauthorized()
        {
            var anonClient = _factory.CreateClient();

            var response = await anonClient.GetAsync(ValidAdminQuery());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetAdminEventsReport_SinAccionReporteVerGlobal_ReturnsForbidden()
        {
            var msg = new HttpRequestMessage(HttpMethod.Get, ValidAdminQuery());
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-admin-reports");

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetAdminEventsReport_ConAccion_ReturnsOk_WithExpectedShape()
        {
            var expected = new ReporteAdminEventosResponseDto
            {
                FechaDesde = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FechaHasta = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                AclaracionImporte = "El MVP no procesa pagos reales.",
                Resumen = new ReporteResumenDto { CantidadEventos = 1, EntradasEmitidas = 2 },
                Eventos = new List<ReporteAdminEventoDetalleDto>
                {
                    new() { EventId = "event-1", Nombre = "Festival", EntradasEmitidas = 2, OrganizadorPersonaId = "persona-organizador-1" },
                },
            };

            _factory.MockReporteService
                .Setup(s => s.GetAdminEventsReportAsync(It.IsAny<ReporteAdminEventosFilterDto>()))
                .ReturnsAsync(expected);

            var response = await _client.GetAsync(ValidAdminQuery());
            var result = await response.Content.ReadFromJsonAsync<ReporteAdminEventosResponseDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(result);
            Assert.Equal(1, result!.Resumen.CantidadEventos);
            Assert.Single(result.Eventos);
            Assert.Equal("event-1", result.Eventos[0].EventId);
            Assert.Equal("persona-organizador-1", result.Eventos[0].OrganizadorPersonaId);
        }

        [Fact]
        public async Task GetAdminEventsReport_BindsFechasEstadoCategoriaOrganizador_SinEventIdNiTicketTypeId()
        {
            ReporteAdminEventosFilterDto? captured = null;
            _factory.MockReporteService
                .Setup(s => s.GetAdminEventsReportAsync(It.IsAny<ReporteAdminEventosFilterDto>()))
                .Callback<ReporteAdminEventosFilterDto>(f => captured = f)
                .ReturnsAsync(new ReporteAdminEventosResponseDto());

            var response = await _client.GetAsync(
                ValidAdminQuery("&estado=Publicado&categoria=Musica&organizadorPersonaId=persona-1"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(captured);
            Assert.Equal(Event.EventEffectiveStatus.Publicado, captured!.Estado);
            Assert.Equal(Event.EventCategory.Musica, captured.Categoria);
            Assert.Equal("persona-1", captured.OrganizadorPersonaId);
        }

        [Fact]
        public async Task GetAdminEventsReport_RangoInvalido_ReturnsBadRequest_WithReportRangeInvalidCode()
        {
            _factory.MockReporteService
                .Setup(s => s.GetAdminEventsReportAsync(It.IsAny<ReporteAdminEventosFilterDto>()))
                .ThrowsAsync(new ReporteRangoInvalidoException("El rango no puede exceder 366 días."));

            var response = await _client.GetAsync(ValidAdminQuery());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REPORT_RANGE_INVALID", body.GetProperty("code").GetString());
        }

        // ==================================================================
        // GET /api/reports/admin/security-audits (docs/api-mvp-plan.md §11.3)
        // ==================================================================

        [Fact]
        public async Task GetSecurityAuditsReport_Anonymous_ReturnsUnauthorized()
        {
            var anonClient = _factory.CreateClient();

            var response = await anonClient.GetAsync("/api/reports/admin/security-audits");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetSecurityAuditsReport_SinAccionReporteVerGlobal_ReturnsForbidden()
        {
            var msg = new HttpRequestMessage(HttpMethod.Get, "/api/reports/admin/security-audits");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-audit-reports");

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetSecurityAuditsReport_ConAccion_ReturnsOk_WithExpectedShape()
        {
            var expected = new SecurityAuditReporteResponseDto
            {
                FechaDesde = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FechaHasta = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Auditorias = new List<SecurityAuditReporteDto>
                {
                    new() { Operacion = "ROL_ASIGNAR_ACCION", ActorUsuarioId = "usuario-1", ActorEmail = "admin@test.com", TargetTipo = "RolAccion", TargetId = "ORGANIZADOR/EVENTO_CREAR" },
                },
            };

            _factory.MockSecurityAuditReportService
                .Setup(s => s.GetSecurityAuditsReportAsync(It.IsAny<SecurityAuditReportFilterDto>()))
                .ReturnsAsync(expected);

            var response = await _client.GetAsync("/api/reports/admin/security-audits");
            var result = await response.Content.ReadFromJsonAsync<SecurityAuditReporteResponseDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(result);
            Assert.Single(result!.Auditorias);
            Assert.Equal("RolAccion", result.Auditorias[0].TargetTipo);
        }

        [Fact]
        public async Task GetSecurityAuditsReport_BindsFiltros()
        {
            SecurityAuditReportFilterDto? captured = null;
            _factory.MockSecurityAuditReportService
                .Setup(s => s.GetSecurityAuditsReportAsync(It.IsAny<SecurityAuditReportFilterDto>()))
                .Callback<SecurityAuditReportFilterDto>(f => captured = f)
                .ReturnsAsync(new SecurityAuditReporteResponseDto());

            var response = await _client.GetAsync(
                "/api/reports/admin/security-audits?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z&operacion=USUARIO_ASIGNAR_ROL&actorUsuarioId=usuario-1&targetTipo=UsuarioRol&targetId=usuario-2/ORGANIZADOR");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(captured);
            Assert.Equal("USUARIO_ASIGNAR_ROL", captured!.Operacion);
            Assert.Equal("usuario-1", captured.ActorUsuarioId);
            Assert.Equal(SecurityAuditTargetTipo.UsuarioRol, captured.TargetTipo);
            Assert.Equal("usuario-2/ORGANIZADOR", captured.TargetId);
        }

        [Fact]
        public async Task GetSecurityAuditsReport_TargetTipoInexistente_ReturnsValidationError400()
        {
            var response = await _client.GetAsync("/api/reports/admin/security-audits?targetTipo=NoExiste");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task GetSecurityAuditsReport_RangoInvalido_ReturnsBadRequest_WithReportRangeInvalidCode()
        {
            _factory.MockSecurityAuditReportService
                .Setup(s => s.GetSecurityAuditsReportAsync(It.IsAny<SecurityAuditReportFilterDto>()))
                .ThrowsAsync(new ReporteRangoInvalidoException("El rango no puede exceder 366 días."));

            var response = await _client.GetAsync("/api/reports/admin/security-audits");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REPORT_RANGE_INVALID", body.GetProperty("code").GetString());
        }

        // ==================================================================
        // GET /api/reports/organizer/sales (docs/api-mvp-plan.md §11)
        // ==================================================================

        private static string ValidSalesQuery(string? extra = null) =>
            "/api/reports/organizer/sales?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z" + (extra ?? "");

        [Fact]
        public async Task GetOrganizerSalesReport_Anonymous_ReturnsUnauthorized()
        {
            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync(ValidSalesQuery());
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetOrganizerSalesReport_SinAccionReporteVerPropio_ReturnsForbidden()
        {
            var msg = new HttpRequestMessage(HttpMethod.Get, ValidSalesQuery());
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-sales");

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetOrganizerSalesReport_ConAccion_ReturnsOk_WithExpectedShape()
        {
            var expected = new VentasReporteResponseDto
            {
                FechaDesde = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FechaHasta = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                AclaracionImporte = "El MVP no procesa pagos reales.",
                Resumen = new VentasResumenDto { CantidadCompras = 3, EntradasEmitidas = 5, ImporteEmitido = 500m, ClientesUnicos = 2 },
                SerieTemporal = new List<VentasSerieBucketDto> { new() { Etiqueta = "01/01", CantidadCompras = 3 } },
                TopEventos = new List<VentasTopEventoDto> { new() { EventoId = "event-1", EventoNombre = "Festival", ImporteEmitido = 500m } },
                PorCategoria = new List<VentasCategoriaDto> { new() { Categoria = "Musica", ImporteEmitido = 500m, PorcentajeDelImporteTotal = 100 } },
            };

            _factory.MockVentasReporteService
                .Setup(s => s.GetOrganizerSalesReportAsync("test-uid-123", It.IsAny<VentasOrganizerFilterDto>()))
                .ReturnsAsync(expected);

            var response = await _client.GetAsync(ValidSalesQuery());
            var content = await response.Content.ReadAsStringAsync();
            var result = await response.Content.ReadFromJsonAsync<VentasReporteResponseDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(result);
            Assert.Equal(3, result!.Resumen.CantidadCompras);
            Assert.Single(result.TopEventos);
            Assert.DoesNotContain("test-uid-123", content);
            Assert.DoesNotContain("persona-reports-test", content);
        }

        [Fact]
        public async Task GetOrganizerSalesReport_BindsFechasEventIdCategoriaTicketTypeId()
        {
            VentasOrganizerFilterDto? captured = null;
            _factory.MockVentasReporteService
                .Setup(s => s.GetOrganizerSalesReportAsync(It.IsAny<string>(), It.IsAny<VentasOrganizerFilterDto>()))
                .Callback<string, VentasOrganizerFilterDto>((_, f) => captured = f)
                .ReturnsAsync(new VentasReporteResponseDto());

            var response = await _client.GetAsync(
                "/api/reports/organizer/sales?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z&eventId=event-1&categoria=Musica&ticketTypeId=tipo-1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(captured);
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), captured!.FechaDesde);
            Assert.Equal("event-1", captured.EventId);
            Assert.Equal(Event.EventCategory.Musica, captured.Categoria);
            Assert.Equal("tipo-1", captured.TicketTypeId);
        }

        [Fact]
        public async Task GetOrganizerSalesReport_RangoInvalido_ReturnsBadRequest_WithReportRangeInvalidCode()
        {
            _factory.MockVentasReporteService
                .Setup(s => s.GetOrganizerSalesReportAsync("test-uid-123", It.IsAny<VentasOrganizerFilterDto>()))
                .ThrowsAsync(new ReporteRangoInvalidoException("El rango no puede exceder 366 días."));

            var response = await _client.GetAsync(ValidSalesQuery());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REPORT_RANGE_INVALID", body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task GetOrganizerSalesReport_FiltroInvalido_ReturnsBadRequest_WithReportFilterInvalidCode()
        {
            _factory.MockVentasReporteService
                .Setup(s => s.GetOrganizerSalesReportAsync("test-uid-123", It.IsAny<VentasOrganizerFilterDto>()))
                .ThrowsAsync(new ReporteFiltroInvalidoException("'ticketTypeId' requiere 'eventId'."));

            var response = await _client.GetAsync(ValidSalesQuery());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REPORT_FILTER_INVALID", body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task GetOrganizerSalesReport_EventoAjeno_ReturnsForbidden()
        {
            _factory.MockVentasReporteService
                .Setup(s => s.GetOrganizerSalesReportAsync("test-uid-123", It.IsAny<VentasOrganizerFilterDto>()))
                .ThrowsAsync(new EventOwnershipException("event-ajeno", "test-uid-123"));

            var response = await _client.GetAsync(ValidSalesQuery("&eventId=event-ajeno"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetOrganizerSalesReport_EventoInexistente_ReturnsNotFound()
        {
            _factory.MockVentasReporteService
                .Setup(s => s.GetOrganizerSalesReportAsync("test-uid-123", It.IsAny<VentasOrganizerFilterDto>()))
                .ThrowsAsync(new EventNotFoundException("event-inexistente"));

            var response = await _client.GetAsync(ValidSalesQuery("&eventId=event-inexistente"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ==================================================================
        // GET /api/reports/admin/sales (docs/api-mvp-plan.md §11)
        // ==================================================================

        private static string ValidAdminSalesQuery(string? extra = null) =>
            "/api/reports/admin/sales?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z" + (extra ?? "");

        [Fact]
        public async Task GetAdminSalesReport_Anonymous_ReturnsUnauthorized()
        {
            var anonClient = _factory.CreateClient();
            var response = await anonClient.GetAsync(ValidAdminSalesQuery());
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetAdminSalesReport_SinAccionReporteVerGlobal_ReturnsForbidden()
        {
            var msg = new HttpRequestMessage(HttpMethod.Get, ValidAdminSalesQuery());
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-admin-sales");

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetAdminSalesReport_ConAccion_ReturnsOk_WithExpectedShape()
        {
            var expected = new VentasReporteResponseDto
            {
                FechaDesde = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FechaHasta = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Resumen = new VentasResumenDto { CantidadCompras = 1, EntradasEmitidas = 1, ImporteEmitido = 10m },
            };

            _factory.MockVentasReporteService
                .Setup(s => s.GetAdminSalesReportAsync(It.IsAny<VentasAdminFilterDto>()))
                .ReturnsAsync(expected);

            var response = await _client.GetAsync(ValidAdminSalesQuery());
            var result = await response.Content.ReadFromJsonAsync<VentasReporteResponseDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(result);
            Assert.Equal(1, result!.Resumen.CantidadCompras);
        }

        [Fact]
        public async Task GetAdminSalesReport_BindsFechasOrganizadorEventIdCategoria()
        {
            VentasAdminFilterDto? captured = null;
            _factory.MockVentasReporteService
                .Setup(s => s.GetAdminSalesReportAsync(It.IsAny<VentasAdminFilterDto>()))
                .Callback<VentasAdminFilterDto>(f => captured = f)
                .ReturnsAsync(new VentasReporteResponseDto());

            var response = await _client.GetAsync(
                ValidAdminSalesQuery("&organizadorPersonaId=persona-1&eventId=event-1&categoria=Deportes"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(captured);
            Assert.Equal("persona-1", captured!.OrganizadorPersonaId);
            Assert.Equal("event-1", captured.EventId);
            Assert.Equal(Event.EventCategory.Deportes, captured.Categoria);
        }

        [Fact]
        public async Task GetAdminSalesReport_RangoInvalido_ReturnsBadRequest_WithReportRangeInvalidCode()
        {
            _factory.MockVentasReporteService
                .Setup(s => s.GetAdminSalesReportAsync(It.IsAny<VentasAdminFilterDto>()))
                .ThrowsAsync(new ReporteRangoInvalidoException("El rango no puede exceder 366 días."));

            var response = await _client.GetAsync(ValidAdminSalesQuery());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REPORT_RANGE_INVALID", body.GetProperty("code").GetString());
        }
    }
}
