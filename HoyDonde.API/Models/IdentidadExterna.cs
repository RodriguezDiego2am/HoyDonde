namespace HoyDonde.API.Models
{
    // Índice inverso identidades_externas/{IdentityProvider}#{ExternalSubjectId} -> UsuarioId.
    // Permite resolver "UID de Firebase -> UsuarioId" en una sola lectura directa por ID, sin
    // query, y fuerza unicidad de ExternalSubjectId por proveedor (creación estricta, ver
    // FirestoreUsuarioRepository). Ver docs/security-refactor-plan.md §2.
    [Google.Cloud.Firestore.FirestoreData]
    public class IdentidadExterna
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string UsuarioId { get; set; } = string.Empty;
    }
}
