using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    public class PermisosEfectivosResponseDto
    {
        public string? UsuarioId { get; set; }
        public string? PersonaId { get; set; }
        public bool UsuarioActivo { get; set; }
        public IReadOnlyList<string> Roles { get; set; } = new List<string>();
        public IReadOnlyList<string> Acciones { get; set; } = new List<string>();
    }
}
