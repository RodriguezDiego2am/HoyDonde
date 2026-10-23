using System;
using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
    public class TicketTransferRequestDto
    {
        // Email of an existing HoyDonde account. Never echoed back in any response or error.
        [Required]
        [EmailAddress]
        [StringLength(254)]
        public string RecipientEmail { get; set; } = string.Empty;
    }

    // Deliberately minimal: no recipient data, no QR token (the new token belongs to the recipient).
    public class TicketTransferResponseDto
    {
        public string TicketId { get; set; } = string.Empty;
        public DateTime TransferredAt { get; set; }
    }
}
