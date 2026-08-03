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
    }
}
