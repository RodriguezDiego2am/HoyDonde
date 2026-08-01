using System;

namespace HoyDonde.API.Models
{
    // Documento de la subcolección usuarios/{UsuarioId}/roles/{RolCodigo}. El ID del documento
    // es el propio código del Rol asignado (mismo patrón que RolAccionAsignacion, Etapa 1).
    [Google.Cloud.Firestore.FirestoreData]
    public class UsuarioRol
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string RolCodigo { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string AssignedBy { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public bool Activo { get; set; } = true;
    }
}
