using System;

namespace HoyDonde.API.Models
{
    // El código (Codigo) es el ID del documento Firestore: inmutable una vez creado.
    // Ver docs/security-refactor-plan.md §3.
    [Google.Cloud.Firestore.FirestoreData]
    public class Rol
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Codigo { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Nombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Descripcion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public bool Activo { get; set; } = true;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? UpdatedAt { get; set; }
    }
}
