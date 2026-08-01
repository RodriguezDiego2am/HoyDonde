using System;

namespace HoyDonde.API.Models
{
    // Documento de la subcolección roles/{Codigo}/acciones/{AccionCodigo}. El ID del documento
    // es el propio código de la Accion asignada (ver docs/security-refactor-plan.md §3).
    [Google.Cloud.Firestore.FirestoreData]
    public class RolAccionAsignacion
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string AccionCodigo { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string AssignedBy { get; set; } = string.Empty;
    }
}
