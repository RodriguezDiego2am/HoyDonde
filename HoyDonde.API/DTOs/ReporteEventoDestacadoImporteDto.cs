namespace HoyDonde.API.DTOs
{
    // Evento destacado por importe emitido dentro del reporte de desempeño (docs/api-mvp-plan.md §11).
    public class ReporteEventoDestacadoImporteDto
    {
        public string EventId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal ImporteEmitido { get; set; }
    }
}
