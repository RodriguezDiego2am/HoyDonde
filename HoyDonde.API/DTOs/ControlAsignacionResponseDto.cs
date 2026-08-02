using System;

namespace HoyDonde.API.DTOs
{
    // Respuesta acotada de POST /api/events/{eventId}/controls/{controlPersonaId}
    // (docs/api-mvp-plan.md §4): solo PersonaId/EventId/metadata de auditoría mínima. Nunca
    // expone el UsuarioId, el UID de Firebase ni ningún dato del proveedor de identidad.
    public class ControlAsignacionResponseDto
    {
        public string ControlPersonaId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string AssignedByPersonaId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
