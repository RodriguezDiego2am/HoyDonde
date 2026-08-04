namespace HoyDonde.API.DTOs
{
    // Agregado sobre todo el conjunto de eventos del reporte (docs/api-mvp-plan.md §11.9). Cuando
    // el filtro trae ticketTypeId, cada evento ya viene acotado a ese tipo antes de sumarse acá.
    public class ReporteResumenDto
    {
        public int CantidadEventos { get; set; }
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

        // Agregado exclusivamente sobre los eventos efectivamente Finalizados del conjunto (nunca
        // sobre eventos futuros/en curso, que no aplican). División por cero -> 0, igual criterio
        // que el resto de los porcentajes de este DTO.
        public int EntradasNoUtilizadasFinalizados { get; set; }
        public double PorcentajeNoUtilizacionFinalizados { get; set; }
    }
}
