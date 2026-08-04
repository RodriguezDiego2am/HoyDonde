using System;
using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Respuesta de GET /api/reports/admin/events (docs/api-mvp-plan.md §11.3): mismo shape que
    // ReporteEventosResponseDto (Organizador), con ReporteAdminEventoDetalleDto (incluye
    // OrganizadorPersonaId) en vez de ReporteEventoDetalleDto.
    public class ReporteAdminEventosResponseDto
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public string AclaracionImporte { get; set; } = string.Empty;
        public ReporteResumenDto Resumen { get; set; } = new();
        public ReporteDestacadosDto Destacados { get; set; } = new();
        public List<ReporteAdminEventoDetalleDto> Eventos { get; set; } = new();
    }
}
