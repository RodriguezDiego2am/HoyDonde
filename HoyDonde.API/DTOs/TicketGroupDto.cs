namespace HoyDonde.API.DTOs
{
    public class TicketGroupDto
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public int CantidadDisponible { get; set; }
    }
}
