using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HoyDonde.API.Controllers
{
    // Controller intencionalmente delgado: ninguna excepción de dominio se mapea acá a un código
    // HTTP. Todas se dejan propagar hasta ExceptionMiddleware (docs/api-mvp-plan.md §5/§11).
    [ApiController]
    [Route("api/reports")]
    public class ReportsController : ControllerBase
    {
        private readonly IReporteService _reporteService;
        private readonly ISecurityAuditReportService _securityAuditReportService;
        private readonly IVentasReporteService _ventasReporteService;

        public ReportsController(
            IReporteService reporteService,
            ISecurityAuditReportService securityAuditReportService,
            IVentasReporteService ventasReporteService)
        {
            _reporteService = reporteService;
            _securityAuditReportService = securityAuditReportService;
            _ventasReporteService = ventasReporteService;
        }

        // docs/api-mvp-plan.md §11: reporte de solo lectura de los eventos propios del
        // Organizador autenticado. Ninguna acción nueva más allá de REPORTE_VER_PROPIO.
        [HttpGet("organizer/events")]
        [Authorize(Policy = Acciones.ReporteVerPropio)]
        public async Task<IActionResult> GetOrganizerEventsReport([FromQuery] ReporteEventosFilterDto filter)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            var result = await _reporteService.GetOrganizerEventsReportAsync(organizerId, filter);
            return Ok(result);
        }

        // docs/api-mvp-plan.md §11.3: reporte agregado de eventos de cualquier organizador. Sin
        // resolución de actor: es un reporte global, no hay ownership que verificar acá (el único
        // filtro de organizador es OrganizadorPersonaId, opcional y arbitrario, aceptado del
        // cliente porque el actor ya es Administrador vía la policy).
        [HttpGet("admin/events")]
        [Authorize(Policy = Acciones.ReporteVerGlobal)]
        public async Task<IActionResult> GetAdminEventsReport([FromQuery] ReporteAdminEventosFilterDto filter)
        {
            var result = await _reporteService.GetAdminEventsReportAsync(filter);
            return Ok(result);
        }

        // docs/api-mvp-plan.md §11.3: auditoría de seguridad, primer corte aprobado.
        [HttpGet("admin/security-audits")]
        [Authorize(Policy = Acciones.ReporteVerGlobal)]
        public async Task<IActionResult> GetSecurityAuditsReport([FromQuery] SecurityAuditReportFilterDto filter)
        {
            var result = await _securityAuditReportService.GetSecurityAuditsReportAsync(filter);
            return Ok(result);
        }

        // docs/api-mvp-plan.md §11: ventas simuladas propias del Organizador, filtradas por
        // Compra.FechaCompra (nunca Event.FechaInicio — ese es el reporte de desempeño de arriba).
        // Reutiliza REPORTE_VER_PROPIO: no se agrega ninguna acción nueva.
        [HttpGet("organizer/sales")]
        [Authorize(Policy = Acciones.ReporteVerPropio)]
        public async Task<IActionResult> GetOrganizerSalesReport([FromQuery] VentasOrganizerFilterDto filter)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            var result = await _ventasReporteService.GetOrganizerSalesReportAsync(organizerId, filter);
            return Ok(result);
        }

        // docs/api-mvp-plan.md §11: ventas simuladas globales, opcionalmente acotadas por
        // organizadorPersonaId. Reutiliza REPORTE_VER_GLOBAL: no se agrega ninguna acción nueva.
        [HttpGet("admin/sales")]
        [Authorize(Policy = Acciones.ReporteVerGlobal)]
        public async Task<IActionResult> GetAdminSalesReport([FromQuery] VentasAdminFilterDto filter)
        {
            var result = await _ventasReporteService.GetAdminSalesReportAsync(filter);
            return Ok(result);
        }

        // UID tomado exclusivamente del token autenticado (nunca de un campo del body/query).
        private string? GetAuthenticatedUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
    }
}
