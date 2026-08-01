using System;

namespace HoyDonde.API.Models
{
    // Individuo del dominio HoyDonde. Id es un UUID propio, generado por la aplicación,
    // independiente del UID de cualquier proveedor de identidad externo. Ver
    // docs/security-refactor-plan.md §2.
    [Google.Cloud.Firestore.FirestoreData]
    public class Persona
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string FullName { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string DNI { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string PhoneNumber { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Email { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public bool IsActive { get; set; } = true;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
