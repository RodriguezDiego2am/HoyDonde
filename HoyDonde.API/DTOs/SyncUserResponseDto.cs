using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    public class SyncUserResponseDto
    {
        public string UsuarioId { get; set; } = string.Empty;
        public string PersonaId { get; set; } = string.Empty;
        public IReadOnlyList<string> Roles { get; set; } = new List<string>();
    }
}
