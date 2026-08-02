namespace HoyDonde.API.DTOs
{
    // Respuesta acotada de GET /api/events/organizer/controls (docs/api-mvp-plan.md §6): un
    // elemento por Control distinto que el organizador autenticado asignó alguna vez, a
    // cualquiera de sus eventos. Nunca expone el UID de Firebase, el ExternalSubjectId ni el
    // UsuarioId interno.
    public class ControlResumenResponseDto
    {
        public string ControlPersonaId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }
}
