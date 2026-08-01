namespace HoyDonde.API.DTOs
{
    // Solo nombre/descripción: el código es inmutable una vez creado
    // (docs/security-refactor-plan.md §6, Etapa 5).
    public class UpdateRolRequestDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
    }
}
