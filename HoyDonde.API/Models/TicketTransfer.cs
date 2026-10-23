using System;

namespace HoyDonde.API.Models
{
    // Audit record of one completed ticket transfer (collection "ticket_transfers"), written in the
    // same Firestore transaction that moves the ticket. It deliberately never stores the QR token
    // (old or new): only the integer versions, which are enough to reconstruct the sequence of
    // rotations without being usable to forge a QR. Also doubles as the data source of the
    // per-sender rate limit (FromPersonaId + Timestamp).
    [Google.Cloud.Firestore.FirestoreData]
    public class TicketTransfer
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TicketId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string FromPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ToPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Google.Cloud.Firestore.FirestoreProperty]
        public int PreviousQrVersion { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public int NewQrVersion { get; set; }
    }
}
