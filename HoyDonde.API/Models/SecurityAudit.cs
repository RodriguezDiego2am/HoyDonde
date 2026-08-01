using System;

namespace HoyDonde.API.Models
{
    // Auditoría de mutaciones de administración de seguridad (docs/security-refactor-plan.md §6,
    // Etapa 5): crear/editar/activar Rol, asignar/quitar Accion de Rol, asignar/quitar Rol de
    // Usuario, activar/desactivar Usuario. Colección propia ("security_audits").
    [Google.Cloud.Firestore.FirestoreData]
    public class SecurityAudit
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ActorUsuarioId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ActorPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Operacion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TargetTipo { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TargetId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Detalle { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
