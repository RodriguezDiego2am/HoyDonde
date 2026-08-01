using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    public class UsuarioResumenResponseDto
    {
        public string UsuarioId { get; set; } = string.Empty;
        public string PersonaId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public IReadOnlyList<string> RolesActivos { get; set; } = new List<string>();
    }
}
