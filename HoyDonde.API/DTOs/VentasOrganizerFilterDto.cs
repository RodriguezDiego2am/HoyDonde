using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Filtro de GET /api/reports/organizer/sales (docs/api-mvp-plan.md §11): a diferencia del
    // reporte de desempeño (ReporteEventosFilterDto, que filtra por Event.FechaInicio), este
    // reporte filtra por Compra.FechaCompra -cuándo se vendió, no cuándo ocurre el evento-. Ambas
    // fechas son obligatorias (validadas en ReporteFiltroValidator.ValidateRango, reutilizado tal
    // cual). Nunca acepta organizadorPersonaId: sale siempre de IAuthenticatedPersonaResolver.
    public class VentasOrganizerFilterDto
    {
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public string? EventId { get; set; }
        public Event.EventCategory? Categoria { get; set; }

        // Requiere EventId (ReporteFiltroValidator.ValidateTicketTypeRequiresEventId, reutilizado).
        public string? TicketTypeId { get; set; }
    }
}
