using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
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
