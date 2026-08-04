namespace HoyDonde.API.DTOs
{
    // Fila del ranking "Top 5 por importe emitido" del reporte de desempeño (docs/api-mvp-plan.md
    // §11). Mismo criterio de orden determinístico que VentasTopEventoDto: 1) ImporteEmitido desc,
    // 2) EntradasEmitidas desc, 3) Nombre (ordinal), 4) EventId (ordinal) como desempate final.
    public class ReporteTopEventoDto
    {
        public string EventId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal ImporteEmitido { get; set; }
        public int EntradasEmitidas { get; set; }
    }
}
