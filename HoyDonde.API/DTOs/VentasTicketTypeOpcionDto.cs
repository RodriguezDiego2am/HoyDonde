namespace HoyDonde.API.DTOs
{
    // Opción seleccionable de tipo de entrada dentro de VentasFiltrosDisponiblesDto
    // (docs/api-mvp-plan.md §11.11): solo Id/Nombre, sin métricas.
    public class VentasTicketTypeOpcionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }
}
