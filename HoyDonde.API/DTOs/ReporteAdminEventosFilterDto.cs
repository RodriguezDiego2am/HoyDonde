using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Filtro de GET /api/reports/admin/events (docs/api-mvp-plan.md §11.3). Deliberadamente sin
    // eventId/ticketTypeId: ese drill-down puntual no es el propósito de este reporte agregado
    // (ver nota del endpoint en el plan). OrganizadorPersonaId es opcional y arbitrario -solo
    // Admin-: a diferencia de ReporteEventosFilterDto (Organizador), acá SÍ se acepta del cliente.
    public class ReporteAdminEventosFilterDto
    {
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public Event.EventEffectiveStatus? Estado { get; set; }
        public Event.EventCategory? Categoria { get; set; }

        public string? OrganizadorPersonaId { get; set; }
    }
}
