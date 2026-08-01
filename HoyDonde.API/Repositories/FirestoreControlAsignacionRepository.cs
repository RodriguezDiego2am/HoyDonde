using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class FirestoreControlAsignacionRepository : IControlAsignacionRepository
    {
        private readonly FirestoreDb _firestore;
        private const string CollectionName = "control_asignaciones";

        public FirestoreControlAsignacionRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        private static string BuildId(string controlPersonaId, string eventId) => $"{controlPersonaId}_{eventId}";

        // Idempotente incluso bajo llamadas concurrentes para el mismo (ControlPersonaId, EventId):
        // la lectura y el Create ocurren dentro de la misma transacción Firestore, así que dos
        // llamadas simultáneas nunca pueden leer ambas "no existe" y crear ambas el documento (a
        // diferencia de un GetSnapshotAsync + CreateAsync fuera de transacción). Si dos
        // transacciones compiten, Firestore aborta y reintenta automáticamente una de ellas; al
        // reintentar, esa transacción ve el documento ya creado por la otra y no vuelve a
        // escribir nada (mismo patrón que FirestoreUsuarioRepository.ProvisionarAsync).
        public async Task AsignarAsync(string controlPersonaId, string eventId, string assignedByPersonaId)
        {
            var docRef = _firestore.Collection(CollectionName).Document(BuildId(controlPersonaId, eventId));

            await _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (snapshot.Exists) return;

                var asignacion = new ControlAsignacion
                {
                    Id = BuildId(controlPersonaId, eventId),
                    ControlPersonaId = controlPersonaId,
                    EventId = eventId,
                    AssignedByPersonaId = assignedByPersonaId,
                };

                transaction.Create(docRef, asignacion);
            });
        }

        public async Task<bool> ExisteAsignacionAsync(string controlPersonaId, string eventId)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(BuildId(controlPersonaId, eventId)).GetSnapshotAsync();
            return snapshot.Exists;
        }
    }
}
