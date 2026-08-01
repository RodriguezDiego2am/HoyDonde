using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    public class SyncUserResponseDto
    {
        public string UsuarioId { get; set; } = string.Empty;
        public string PersonaId { get; set; } = string.Empty;
        public IReadOnlyList<string> Roles { get; set; } = new List<string>();

        // Si es true, el frontend debe forzar la renovación del ID token (getIdToken(true))
        // antes de llamar endpoints legacy con [Authorize(Roles = Roles.Cliente)].
        public bool ClaimsUpdated { get; set; }
    }
}
