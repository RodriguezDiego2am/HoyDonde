using System;

namespace HoyDonde.API.Models
{
    // Registro de una identidad de proveedor externo que quedó huérfana: se creó en
    // IIdentityProvider pero el aprovisionamiento del modelo nuevo (o el claim legacy) falló
    // después, Y el intento de compensación (DeleteIdentityAsync) también falló. Requiere
    // intervención manual. Ver docs/security-refactor-plan.md §2.2/§7, Etapa 3.
    [Google.Cloud.Firestore.FirestoreData]
    public class IdentidadHuerfana
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string IdentityProvider { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ExternalSubjectId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Email { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string RolCodigoSolicitado { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ErrorOriginal { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ErrorCompensacion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
