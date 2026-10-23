using System;

namespace HoyDonde.API.Models
{
    // Cuenta de seguridad. Id es un UUID propio del módulo de seguridad, distinto tanto de
    // Persona.Id como del UID del proveedor externo (ExternalSubjectId). Ver
    // docs/security-refactor-plan.md §2.
    [Google.Cloud.Firestore.FirestoreData]
    public class Usuario
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string PersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string IdentityProvider { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ExternalSubjectId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Email { get; set; } = string.Empty;

        // Lowercased, trimmed copy of Email used for case-insensitive lookups (Firestore has no
        // case-insensitive queries). Always derive it with NormalizarEmail; empty on documents
        // not yet backfilled by migrate-usuario-email-normalizado.
        [Google.Cloud.Firestore.FirestoreProperty]
        public string EmailNormalizado { get; set; } = string.Empty;

        public static string NormalizarEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

        [Google.Cloud.Firestore.FirestoreProperty]
        public bool IsActive { get; set; } = true;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
