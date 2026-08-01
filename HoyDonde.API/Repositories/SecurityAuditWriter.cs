using Google.Cloud.Firestore;
using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    // Escritura centralizada de security_audits (docs/security-refactor-plan.md §6, Etapa 5),
    // siempre dentro de la misma transacción Firestore que la mutación auditada: si la
    // transacción se descarta (por ejemplo, el guard del último Administrador), la auditoría
    // nunca queda escrita.
    internal static class SecurityAuditWriter
    {
        private const string CollectionName = "security_audits";

        public static void Write(FirestoreDb firestore, Transaction transaction, SecurityAudit entry)
        {
            var auditRef = firestore.Collection(CollectionName).Document(entry.Id);
            transaction.Create(auditRef, entry);
        }
    }
}
