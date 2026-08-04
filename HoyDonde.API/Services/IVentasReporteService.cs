using HoyDonde.API.DTOs;

namespace HoyDonde.API.Services
{
    public interface IVentasReporteService
    {
        // GET /api/reports/organizer/sales (docs/api-mvp-plan.md §11): ventas simuladas propias
        // del organizador autenticado, filtradas por Compra.FechaCompra (nunca Event.FechaInicio).
        // actorId es el UID de Firebase; se resuelve siempre a PersonaId vía
        // IAuthenticatedPersonaResolver, nunca aceptado del cliente.
        Task<VentasReporteResponseDto> GetOrganizerSalesReportAsync(string actorId, VentasOrganizerFilterDto filter);

        // GET /api/reports/admin/sales (docs/api-mvp-plan.md §11): ventas simuladas globales,
        // opcionalmente acotadas por organizadorPersonaId (arbitrario, aceptado del cliente porque
        // el actor ya es Administrador vía la policy).
        Task<VentasReporteResponseDto> GetAdminSalesReportAsync(VentasAdminFilterDto filter);
    }
}
