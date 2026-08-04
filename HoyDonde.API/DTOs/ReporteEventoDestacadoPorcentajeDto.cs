namespace HoyDonde.API.DTOs
{
    // Evento destacado por un porcentaje (ocupación o asistencia) dentro del reporte de desempeño
    // (docs/api-mvp-plan.md §11). Separado de ReporteEventoDestacadoImporteDto porque cada
    // destacado del reporte de desempeño representa una única métrica, a diferencia del reporte de
    // ventas (VentasEventoDestacadoDto), donde ambas métricas ya están disponibles juntas.
    public class ReporteEventoDestacadoPorcentajeDto
    {
        public string EventId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public double Porcentaje { get; set; }
    }
}
