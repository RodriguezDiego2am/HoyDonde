using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
    public class TicketBuyRequest
    {
        [Required]
        public string EventoId { get; set; } = string.Empty;

        [Required]
        public string TicketTypeId { get; set; } = string.Empty;

        [Required]
        [Range(1, 10, ErrorMessage = "Puedes comprar entre 1 y 10 tickets por operación.")]
        public int Cantidad { get; set; }
    }
}
