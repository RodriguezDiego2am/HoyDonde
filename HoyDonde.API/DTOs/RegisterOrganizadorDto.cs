using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
    public class RegisterOrganizadorDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "El email es obligatorio.")]
        [EmailAddress(ErrorMessage = "El email no tiene un formato válido.")]
        public string Email { get; set; } = string.Empty;

        // 6 caracteres es el mínimo que exige Firebase Authentication para una contraseña.
        [Required(AllowEmptyStrings = false, ErrorMessage = "La contraseña es obligatoria.")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        public string Password { get; set; } = string.Empty;
    }
}
