namespace HoyDonde.API.DTOs
{
    // Opción seleccionable de evento dentro de VentasFiltrosDisponiblesDto (docs/api-mvp-plan.md
    // §11.11): deliberadamente mínima (solo Id/Nombre, sin métricas) — es para poblar un selector,
    // no una fila de ranking. Nunca expone OrganizadorPersonaId.
    public class VentasEventoOpcionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }
}
