using System;

namespace HoyDonde.API.DTOs
{
    public class TicketResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string EventoId { get; set; } = string.Empty;
        public string TicketTypeId { get; set; } = string.Empty;
        public string ClientePersonaId { get; set; } = string.Empty;
        public DateTime FechaCompra { get; set; }
    }
}
