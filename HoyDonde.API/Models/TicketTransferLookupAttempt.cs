using System;

namespace HoyDonde.API.Models
{
    // One recipient-email lookup attempt of a ticket transfer (collection
    // "ticket_transfer_lookup_attempts"). It exists only to bound email enumeration: the row is
    // reserved before the lookup and deleted when the lookup did not fail, so the rows still alive
    // inside the window are the sender's failed lookups. It deliberately stores no email (not even
    // a hash), no ticket and no outcome: only who asked and when (FromPersonaId + Timestamp).
    [Google.Cloud.Firestore.FirestoreData]
    public class TicketTransferLookupAttempt
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [Google.Cloud.Firestore.FirestoreProperty]
        public string FromPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
