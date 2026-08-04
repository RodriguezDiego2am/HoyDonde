namespace HoyDonde.API.DTOs
{
    // Fila del ranking "Top eventos por importe emitido" (docs/api-mvp-plan.md §11). Máximo 5
    // filas, orden determinístico: 1) ImporteEmitido desc, 2) EntradasEmitidas desc, 3) EventoNombre
    // (ordinal), 4) EventoId (ordinal) como último desempate.
    public class VentasTopEventoDto
    {
        public string EventoId { get; set; } = string.Empty;
        public string EventoNombre { get; set; } = string.Empty;
        public int CantidadCompras { get; set; }
        public int EntradasEmitidas { get; set; }
        public decimal ImporteEmitido { get; set; }
        public decimal ImportePromedioCompra { get; set; }
    }
}
