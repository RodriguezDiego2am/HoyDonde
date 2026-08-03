using HoyDonde.API.DTOs;

namespace HoyDonde.API.Services
{
    public interface IReporteService
    {
        // GET /api/reports/organizer/events (docs/api-mvp-plan.md §11): reporte de solo lectura de
        // los eventos propios del organizador autenticado. actorId es el UID de Firebase; se
        // resuelve siempre a PersonaId vía IAuthenticatedPersonaResolver, nunca aceptado del
        // cliente (el filtro no tiene ningún campo de organizador).
        Task<ReporteEventosResponseDto> GetOrganizerEventsReportAsync(string actorId, ReporteEventosFilterDto filter);

        // GET /api/reports/admin/events (docs/api-mvp-plan.md §11.3): reporte agregado de eventos
        // de cualquier organizador. OrganizadorPersonaId es el único filtro de ownership y, a
        // diferencia del reporte del Organizador, viene del cliente (uso administrativo global,
        // sin IAuthenticatedPersonaResolver de por medio).
        Task<ReporteAdminEventosResponseDto> GetAdminEventsReportAsync(ReporteAdminEventosFilterDto filter);
    }
}
