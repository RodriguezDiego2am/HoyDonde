using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
    // DTO de ENTRADA exclusivamente, usado por EventCreateRequest/EventUpdateRequest para
    // crear/editar un evento. Deliberadamente sin Id: el TicketTypeId lo genera siempre el
    // servidor (EventService), nunca el cliente. Para leer un tipo de ticket ya persistido
    // (con su Id real) usar TicketTypeResponseDto (EventResponse), no este DTO.
    public class TicketGroupDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "El nombre del tipo de ticket es obligatorio.")]
        public string Nombre { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "El precio no puede ser negativo.")]
        public decimal Precio { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "La cantidad disponible debe ser al menos 1.")]
        public int CantidadDisponible { get; set; }
    }
}
