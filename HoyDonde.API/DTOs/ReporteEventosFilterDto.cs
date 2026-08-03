using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Filtro de GET /api/reports/organizer/events (docs/api-mvp-plan.md §11). FechaDesde/
    // FechaHasta son obligatorias (validadas en ReporteFiltroValidator, no acá vía
    // DataAnnotations): DateTime? permite distinguir "ausente" de "DateTime.MinValue". Nunca
    // acepta organizadorPersonaId: el organizador sale siempre de IAuthenticatedPersonaResolver.
    public class ReporteEventosFilterDto
    {
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        // Efectivo (incluye Finalizado, no persistido) para poder distinguir un Publicado vigente
        // de uno ya finalizado, igual criterio que Event.GetEstadoEfectivo.
        public Event.EventEffectiveStatus? Estado { get; set; }
        public Event.EventCategory? Categoria { get; set; }

        public string? EventId { get; set; }

        // Requiere EventId (validado en ReporteFiltroValidator.ValidateTicketTypeRequiresEventId).
        public string? TicketTypeId { get; set; }
    }
}
