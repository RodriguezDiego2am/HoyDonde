using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
    public class RegisterControlDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "El nombre de usuario es obligatorio.")]
        public string UserName { get; set; } = string.Empty;

        // 6 caracteres es el mínimo que exige Firebase Authentication para una contraseña.
        [Required(AllowEmptyStrings = false, ErrorMessage = "La contraseña es obligatoria.")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        public string Password { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "El evento es obligatorio.")]
        public string EventId { get; set; } = string.Empty; // Evento al que pertenece el Control
    }
}
