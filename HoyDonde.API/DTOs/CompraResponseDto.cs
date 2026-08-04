using System;
using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Respuesta de POST /api/tickets/buy (docs/api-mvp-plan.md §14): agrupa la Compra recién
    // creada con sus Ticket asociados. Nunca expone ClientePersonaId, UID de Firebase, UsuarioId
    // ni ExternalSubjectId — el cliente ya sabe quién es sin necesidad de que la respuesta lo diga.
    public class CompraResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string EventoId { get; set; } = string.Empty;
        public string EventoNombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public DateTime FechaCompra { get; set; }
        public int CantidadEntradas { get; set; }
        public decimal ImporteTotal { get; set; }
        public bool PagoSimulado { get; set; }
        public List<TicketResponseDto> Tickets { get; set; } = new();
    }
}
