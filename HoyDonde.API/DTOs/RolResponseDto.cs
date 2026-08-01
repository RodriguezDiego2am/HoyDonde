using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    public class RolResponseDto
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public IReadOnlyList<string> Acciones { get; set; } = new List<string>();
    }
}
