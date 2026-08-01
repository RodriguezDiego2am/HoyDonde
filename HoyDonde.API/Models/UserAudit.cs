using Google.Cloud.Firestore;
using System;

namespace HoyDonde.API.Models
{
    [FirestoreData]
    public class UserAudit
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [FirestoreProperty]
        public string AffectedUserId { get; set; } = string.Empty;

        [FirestoreProperty]
        public string ActionType { get; set; } = string.Empty; // CREATE, UPDATE, DELETE, LOGIN

        [FirestoreProperty]
        public string ActionDescription { get; set; } = string.Empty;

        [FirestoreProperty]
        public string PerformedBy { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [FirestoreProperty]
        public string SnapshotData { get; set; } = string.Empty; // JSON representation of the entity state
    }
}
