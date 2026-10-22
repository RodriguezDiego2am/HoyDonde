namespace HoyDonde.API.DTOs
{
    // Conteo operativo por estado. ImporteIniciado es informativo y nunca implica cobro/emitido.
    public class VentasEstadoCompraDto
    {
        public string EstadoCompra { get; set; } = string.Empty;
        public int CantidadCompras { get; set; }
        public decimal ImporteIniciado { get; set; }
    }
}
