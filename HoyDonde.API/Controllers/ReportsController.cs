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

        public ReportsController(IReporteService reporteService, ISecurityAuditReportService securityAuditReportService)
        {
            _reporteService = reporteService;
            _securityAuditReportService = securityAuditReportService;
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

        // UID tomado exclusivamente del token autenticado (nunca de un campo del body/query).
        private string? GetAuthenticatedUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
    }
}
