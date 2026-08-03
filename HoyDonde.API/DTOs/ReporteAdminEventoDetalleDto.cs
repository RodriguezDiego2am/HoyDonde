namespace HoyDonde.API.DTOs
{
    // Detalle de un evento dentro del reporte Admin (docs/api-mvp-plan.md §11.3): mismo shape que
    // ReporteEventoDetalleDto (Organizador) más OrganizadorPersonaId -el Admin ve eventos de
    // cualquier organizador, así que necesita el identificador para poder distinguirlos/filtrar;
    // el frontend lo resuelve a nombre/email por su cuenta, nunca se interpreta como un dato
    // sensible nuevo (ya se expone igual en GET /api/security/usuarios).
    public class ReporteAdminEventoDetalleDto : ReporteEventoDetalleDto
    {
        public string OrganizadorPersonaId { get; set; } = string.Empty;
    }
}
