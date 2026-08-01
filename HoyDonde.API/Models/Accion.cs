using System;

namespace HoyDonde.API.Models
{
    // El código (Codigo) es el ID del documento Firestore: inmutable una vez creado.
    // El catálogo de códigos lo controla el desarrollo; Descripcion/Activo son editables
    // por API. Ver docs/security-refactor-plan.md §7.
    [Google.Cloud.Firestore.FirestoreData]
    public class Accion
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Codigo { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Descripcion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public bool Activo { get; set; } = true;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
