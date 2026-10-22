using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IPaymentWebhookInbox
    {
        Task PersistAsync(string externalPaymentId, DateTime receivedAt);
    }

    // The durable inbox contains only provider ids and sanitized processing state: never the webhook body or signature.
    public sealed class PaymentWebhookInbox : IPaymentWebhookInbox
    {
        private readonly FirestoreDb _firestore;
        public PaymentWebhookInbox(FirestoreDb firestore) => _firestore = firestore;

        public Task PersistAsync(string externalPaymentId, DateTime receivedAt)
        {
            if (string.IsNullOrWhiteSpace(externalPaymentId)) throw new ArgumentException("Se requiere el identificador de pago.", nameof(externalPaymentId));
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalPaymentId))).ToLowerInvariant();
            var reference = _firestore.Collection("payment_webhook_inbox").Document(id);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var current = await transaction.GetSnapshotAsync(reference);
                var utcReceivedAt = receivedAt.Kind == DateTimeKind.Utc ? receivedAt : receivedAt.ToUniversalTime();
                if (!current.Exists)
                {
                    transaction.Create(reference, new PaymentWebhookInboxItem
                    {
                        Id = id,
                        ExternalPaymentId = externalPaymentId,
                        ReceivedAt = utcReceivedAt,
                        ProcessingStatus = "Pending",
                        Revision = 1,
                    });
                    return;
                }

                var item = current.ConvertTo<PaymentWebhookInboxItem>();
                // Notifications carry no authoritative state; any accepted later delivery must cause a
                // fresh GET. Pending is idempotent and a completed/claimed record is safely requeued.
                transaction.Set(reference, new PaymentWebhookInboxItem
                {
                    Id = id,
                    ExternalPaymentId = item.ExternalPaymentId,
                    ReceivedAt = utcReceivedAt > item.ReceivedAt ? utcReceivedAt : item.ReceivedAt,
                    ProcessingStatus = "Pending",
                    Revision = item.Revision + 1,
                    ProcessingRevision = null,
                }, SetOptions.MergeAll);
            });
        }
    }
}
