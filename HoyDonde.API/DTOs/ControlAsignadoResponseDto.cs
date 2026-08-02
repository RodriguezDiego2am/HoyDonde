using System;

namespace HoyDonde.API.DTOs
{
    // Respuesta acotada de GET /api/events/{eventId}/controls (docs/api-mvp-plan.md §6): los
    // Controles asignados a ese evento propio, con la metadata mínima de la asignación. Nunca
    // expone el UID de Firebase, el ExternalSubjectId ni el UsuarioId interno.
    public class ControlAsignadoResponseDto
    {
        public string ControlPersonaId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public string AssignedByPersonaId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
