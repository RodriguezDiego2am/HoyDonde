using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    public class SyncUserResponseDto
    {
        public string UsuarioId { get; set; } = string.Empty;
        public string PersonaId { get; set; } = string.Empty;
        public IReadOnlyList<string> Roles { get; set; } = new List<string>();

        // Códigos de Accion efectivos del Usuario sincronizado, resueltos vía IPermissionService
        // (Usuario -> UsuarioRol -> Rol -> RolAccion -> Accion), únicos y en orden determinístico
        // (ordinal ascendente). El frontend los usa para decidir qué opciones mostrar; el backend
        // y sus policies siguen siendo la única autoridad real de autorización.
        public IReadOnlyList<string> Acciones { get; set; } = new List<string>();
    }
}
