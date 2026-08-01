using System;

namespace HoyDonde.API.Models
{
    // Reemplaza los escalares legacy Control.EventId/Control.OrganizadorId
    // (docs/security-refactor-plan.md §4, Etapa 4): colección top-level con ID determinístico
    // "{ControlPersonaId}_{EventId}", que habilita de entrada que un mismo Control tenga
    // asignaciones a múltiples eventos.
    [Google.Cloud.Firestore.FirestoreData]
    public class ControlAsignacion
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ControlPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventId { get; set; } = string.Empty;

        // PersonaId del organizador que asignó este Control (auditoría mínima; nunca UID).
        [Google.Cloud.Firestore.FirestoreProperty]
        public string AssignedByPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
