using HoyDonde.API.Models;

public class TicketType
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public int EventoId { get; set; }
    public virtual Event Evento { get; set; } = null!;
    public int CantidadDisponible { get; set; }
    public virtual List<Ticket> TicketsVendidos { get; set; } = new();
}

