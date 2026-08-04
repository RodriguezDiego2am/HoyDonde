using System;

namespace HoyDonde.API.DTOs
{
    // Un período de la serie temporal de ventas (docs/api-mvp-plan.md §11): la agrupación (día/
    // semana/mes) se calcula en la zona horaria funcional de HoyDonde (America/Argentina/
    // Buenos_Aires, ver Services/ArgentinaTimeZoneProvider.cs), pero PeriodoDesde/PeriodoHasta
    // viajan en UTC (mismo criterio que el resto del contrato HTTP) — son el instante UTC exacto
    // de inicio/fin (exclusivo) del período local. Etiqueta es el único campo pensado para mostrar
    // directamente en la UI/PDF.
    public class VentasSerieBucketDto
    {
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public string Etiqueta { get; set; } = string.Empty;

        public int CantidadCompras { get; set; }
        public int EntradasEmitidas { get; set; }
        public decimal ImporteEmitido { get; set; }
    }
}
