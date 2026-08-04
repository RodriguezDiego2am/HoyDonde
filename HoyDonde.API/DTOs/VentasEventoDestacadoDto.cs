namespace HoyDonde.API.DTOs
{
    // Evento destacado dentro del resumen de ventas (docs/api-mvp-plan.md §11): mayor importe
    // emitido o mayor cantidad de entradas. Ambos valores siempre acompañan al evento -a
    // diferencia de ReporteEventoDestacadoDto (desempeño), que separa porcentaje/importe en dos
    // tipos- porque acá ambas métricas ya están disponibles sin cálculo adicional.
    public class VentasEventoDestacadoDto
    {
        public string EventoId { get; set; } = string.Empty;
        public string EventoNombre { get; set; } = string.Empty;
        public decimal ImporteEmitido { get; set; }
        public int EntradasEmitidas { get; set; }
    }
}
