using System;
using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Respuesta de GET /api/reports/organizer/events (docs/api-mvp-plan.md §11.9). AclaracionImporte
    // es texto fijo: el MVP no procesa pagos reales, así que "importe emitido" nunca se llama
    // "recaudación"/"cobrado"/"ganancia" en ningún lugar de esta respuesta.
    public class ReporteEventosResponseDto
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public string AclaracionImporte { get; set; } = string.Empty;
        public ReporteResumenDto Resumen { get; set; } = new();
        public List<ReporteEventoDetalleDto> Eventos { get; set; } = new();
    }
}
