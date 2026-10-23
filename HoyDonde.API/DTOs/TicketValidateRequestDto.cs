using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
    // Body (not query string) so the bearer-secret QR token never lands in access logs.
    public class TicketValidateRequestDto
    {
        [Required]
        public string QrToken { get; set; } = string.Empty;

        // Optional. When omitted, the event is derived from the ticket that owns the token (the QR
        // carries only the token). When present it must match that ticket's event.
        public string? EventId { get; set; }
    }
}
