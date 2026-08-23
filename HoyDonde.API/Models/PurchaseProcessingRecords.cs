using System;

namespace HoyDonde.API.Models
{
    // Persistence-only foundations for later provider processing. They deliberately contain no
    // provider payload, signature, access token, or other secret material.
    [Google.Cloud.Firestore.FirestoreData]
    public class PaymentRecord
    {
        public enum Classification { Unknown, AwaitingResolution, Approved, NotApprovedTerminal, ReversedOrDisputed }

        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string CompraId { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string ExternalPaymentId { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string RawStatusSanitized { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public Classification Estado { get; set; } = Classification.Unknown;
        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? ApprovedAt { get; set; }
        [Google.Cloud.Firestore.FirestoreProperty(ConverterType = typeof(HoyDonde.API.Converters.DecimalFirestoreConverter))]
        public decimal Importe { get; set; }
        [Google.Cloud.Firestore.FirestoreProperty]
        public string Moneda { get; set; } = string.Empty;
    }

    [Google.Cloud.Firestore.FirestoreData]
    public class PaymentWebhookInboxItem
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string ExternalPaymentId { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime ReceivedAt { get; set; }
        [Google.Cloud.Firestore.FirestoreProperty]
        public string ProcessingStatus { get; set; } = "Pending";
        // A new authenticated notification increments Revision even when it has the same payment id.
        // Workers complete only the revision they claimed, so a later notification cannot be lost.
        [Google.Cloud.Firestore.FirestoreProperty]
        public long Revision { get; set; }
        [Google.Cloud.Firestore.FirestoreProperty]
        public long? ProcessingRevision { get; set; }
    }

    [Google.Cloud.Firestore.FirestoreData]
    public class RefundJob
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string CompraId { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string PaymentRecordId { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public Compra.RefundReason Reason { get; set; }
        [Google.Cloud.Firestore.FirestoreProperty]
        public string Status { get; set; } = "Required";
        [Google.Cloud.Firestore.FirestoreProperty]
        public int Attempts { get; set; }
        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? LastAttemptAt { get; set; }
    }

    [Google.Cloud.Firestore.FirestoreData]
    public class PurchaseAuditEntry
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string CompraId { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime OccurredAt { get; set; }
        [Google.Cloud.Firestore.FirestoreProperty]
        public string Action { get; set; } = string.Empty;
        [Google.Cloud.Firestore.FirestoreProperty]
        public string SanitizedReason { get; set; } = string.Empty;
    }
}
