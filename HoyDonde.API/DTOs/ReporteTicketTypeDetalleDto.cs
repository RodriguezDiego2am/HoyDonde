namespace HoyDonde.API.DTOs
{
    // Desglose por tipo de entrada de un evento (docs/api-mvp-plan.md §11.2). CapacidadInicial acá
    // es siempre una derivación (CantidadDisponible actual + entradas ya emitidas de ese tipo),
    // nunca un dato persistido: no existe un campo de capacidad inicial por TicketType.
    public class ReporteTicketTypeDetalleDto
    {
        public string TicketTypeId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int CapacidadInicial { get; set; }
        public int StockDisponible { get; set; }
        public int EntradasEmitidas { get; set; }
        public int EntradasUsadas { get; set; }
        public int EntradasAnuladas { get; set; }
        public int EntradasPendientes { get; set; }
        public double PorcentajeOcupacion { get; set; }
        public double PorcentajeAsistencia { get; set; }
        public double PorcentajeUtilizacion { get; set; }
        public decimal ImporteEmitido { get; set; }
    }
}
