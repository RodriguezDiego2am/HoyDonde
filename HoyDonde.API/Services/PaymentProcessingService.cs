using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Services
{
    public interface IPaymentProcessingService
    {
        Task ProcessPendingInboxAsync(CancellationToken cancellationToken);
        Task ExpireDueReservationsAsync(CancellationToken cancellationToken);
        Task ProcessRequiredRefundsAsync(CancellationToken cancellationToken);
    }

    // Workers call this service; webhook requests deliberately only persist the inbox and return.
    public sealed class PaymentProcessingService : IPaymentProcessingService
    {
        private readonly FirestoreDb _firestore;
        private readonly IMercadoPagoGateway _gateway;
        private readonly PurchaseService _purchases;
        private readonly ILogger<PaymentProcessingService> _logger;
        public PaymentProcessingService(FirestoreDb firestore, IMercadoPagoGateway gateway, PurchaseService purchases, ILogger<PaymentProcessingService> logger)
        {
            _firestore = firestore; _gateway = gateway; _purchases = purchases; _logger = logger;
        }

        public async Task ProcessPendingInboxAsync(CancellationToken cancellationToken)
        {
            var inbox = await _firestore.Collection("payment_webhook_inbox").WhereEqualTo("ProcessingStatus", "Pending").Limit(50).GetSnapshotAsync(cancellationToken);
            foreach (var document in inbox.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = await ClaimInboxAsync(document.Reference);
                if (item is null) continue;
                try
                {
                    // This GET is intentionally the first provider-dependent action and source of truth.
                    var payment = await _gateway.GetPaymentAsync(item.ExternalPaymentId, cancellationToken);
                    await ProcessPaymentAsync(payment, cancellationToken);
                    await CompleteInboxAsync(document.Reference, item.Revision, "Processed");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Mercado Pago inbox processing failed without logging payment payload.");
                    await CompleteInboxAsync(document.Reference, item.Revision, "Pending");
                }
            }
        }

        public async Task ExpireDueReservationsAsync(CancellationToken cancellationToken)
        {
            var active = await _firestore.Collection("compras").WhereEqualTo("EstadoReserva", Compra.ReservationStatus.Active).Limit(100).GetSnapshotAsync(cancellationToken);
            foreach (var document in active.Documents)
            {
                var purchase = document.ConvertTo<Compra>();
                if (purchase.ExpiresAt is not null && purchase.ExpiresAt <= DateTime.UtcNow)
                    await _purchases.ExpireAsync(purchase.Id, DateTime.UtcNow);
            }
        }

        public async Task ProcessRequiredRefundsAsync(CancellationToken cancellationToken)
        {
            var jobs = new List<DocumentSnapshot>();
            foreach (var status in new[] { "Required", "Pending" })
            {
                var batch = await _firestore.Collection("refund_jobs").WhereEqualTo("Status", status).Limit(50).GetSnapshotAsync(cancellationToken);
                jobs.AddRange(batch.Documents);
            }

            foreach (var document in jobs.GroupBy(job => job.Id).Select(group => group.First()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var job = await ClaimRefundAsync(document.Reference);
                if (job is null) continue;
                try
                {
                    var payment = await _firestore.Collection("payment_records").Document(job.PaymentRecordId).GetSnapshotAsync(cancellationToken);
                    if (!payment.Exists)
                    {
                        await CompleteRefundAsync(document.Reference, job.Attempts, "Failed", job.CompraId);
                        continue;
                    }

                    var record = payment.ConvertTo<PaymentRecord>();
                    // MercadoPagoGateway derives the key from this payment id on every call, including retries.
                    var refund = await _gateway.RefundFullAsync(record.ExternalPaymentId, cancellationToken);
                    var status = RefundStatusFromProvider(refund.Status);
                    await CompleteRefundAsync(document.Reference, job.Attempts, status, job.CompraId);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Mercado Pago refund call failed without logging payment payload.");
                    await CompleteRefundAsync(document.Reference, job.Attempts, "Pending", job.CompraId);
                }
            }
        }

        private async Task ProcessPaymentAsync(MercadoPagoPayment payment, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(payment.ExternalReference)) return;
            var purchaseReference = _firestore.Collection("compras").Document(payment.ExternalReference);
            var purchaseSnapshot = await purchaseReference.GetSnapshotAsync(cancellationToken);
            if (!purchaseSnapshot.Exists) return;
            var purchase = purchaseSnapshot.ConvertTo<Compra>();
            var decision = PaymentResolution.Decide(purchase, payment);
            var recordId = Hash(payment.ExternalPaymentId);
            await _firestore.Collection("payment_records").Document(recordId).SetAsync(new PaymentRecord
            {
                Id = recordId, CompraId = purchase.Id, ExternalPaymentId = payment.ExternalPaymentId,
                RawStatusSanitized = payment.RawStatus, Estado = (PaymentRecord.Classification)decision.Classification,
                ApprovedAt = payment.ApprovedAt, Importe = payment.Amount, Moneda = payment.Currency,
            }, SetOptions.MergeAll, cancellationToken);

            switch (decision.Kind)
            {
                case PaymentResolutionKind.Approve:
                    var transition = await _purchases.ProcessVerifiedApprovalAsync(purchase.Id, payment.ApprovedAt!.Value);
                    if (transition == PurchaseTransition.RefundRequired) await CreateRefundJobAsync(purchase.Id, recordId, cancellationToken);
                    break;
                case PaymentResolutionKind.NotApproved:
                    await _purchases.ProcessNotApprovedAsync(purchase.Id, DateTime.UtcNow);
                    break;
                case PaymentResolutionKind.VerificationException:
                    await purchaseReference.SetAsync(new { EstadoCompra = Compra.PurchaseStatus.VerificationException }, SetOptions.MergeAll, cancellationToken);
                    break;
            }
        }

        private async Task<PaymentWebhookInboxItem?> ClaimInboxAsync(DocumentReference reference)
        {
            return await _firestore.RunTransactionAsync(async transaction =>
            {
                var current = await transaction.GetSnapshotAsync(reference);
                if (!current.Exists) return null;
                var item = current.ConvertTo<PaymentWebhookInboxItem>();
                if (!string.Equals(item.ProcessingStatus, "Pending", StringComparison.Ordinal)) return null;
                item.ProcessingStatus = "Processing";
                item.ProcessingRevision = item.Revision;
                transaction.Set(reference, item, SetOptions.MergeAll);
                return item;
            });
        }

        private Task CompleteInboxAsync(DocumentReference reference, long claimedRevision, string status)
        {
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var current = await transaction.GetSnapshotAsync(reference);
                if (!current.Exists) return;
                var item = current.ConvertTo<PaymentWebhookInboxItem>();
                if (item.Revision != claimedRevision || item.ProcessingRevision != claimedRevision || !string.Equals(item.ProcessingStatus, "Processing", StringComparison.Ordinal))
                    return;
                item.ProcessingStatus = status;
                item.ProcessingRevision = null;
                transaction.Set(reference, item, SetOptions.MergeAll);
            });
        }

        private async Task<RefundJob?> ClaimRefundAsync(DocumentReference reference)
        {
            return await _firestore.RunTransactionAsync(async transaction =>
            {
                var current = await transaction.GetSnapshotAsync(reference);
                if (!current.Exists) return null;
                var job = current.ConvertTo<RefundJob>();
                if (job.Status is not ("Required" or "Pending")) return null;
                job.Status = "InProgress";
                job.Attempts++;
                job.LastAttemptAt = DateTime.UtcNow;
                transaction.Set(reference, job, SetOptions.MergeAll);
                return job;
            });
        }

        private async Task CompleteRefundAsync(DocumentReference reference, int attempt, string status, string compraId)
        {
            var completed = await _firestore.RunTransactionAsync(async transaction =>
            {
                var current = await transaction.GetSnapshotAsync(reference);
                if (!current.Exists) return false;
                var job = current.ConvertTo<RefundJob>();
                if (job.Attempts != attempt || !string.Equals(job.Status, "InProgress", StringComparison.Ordinal)) return false;
                job.Status = status;
                transaction.Set(reference, job, SetOptions.MergeAll);
                return true;
            });
            if (!completed) return;
            var purchaseStatus = status switch
            {
                "Completed" => Compra.PurchaseStatus.RefundCompleted,
                "Failed" => Compra.PurchaseStatus.RefundFailed,
                _ => Compra.PurchaseStatus.RefundPending,
            };
            await _firestore.Collection("compras").Document(compraId).SetAsync(new { RefundStatus = status, EstadoCompra = purchaseStatus }, SetOptions.MergeAll);
        }

        private async Task CreateRefundJobAsync(string compraId, string paymentRecordId, CancellationToken cancellationToken)
        {
            var purchase = (await _firestore.Collection("compras").Document(compraId).GetSnapshotAsync(cancellationToken)).ConvertTo<Compra>();
            var jobId = Hash($"{compraId}:{paymentRecordId}");
            await _firestore.Collection("refund_jobs").Document(jobId).SetAsync(new RefundJob
            {
                Id = jobId, CompraId = compraId, PaymentRecordId = paymentRecordId, Reason = purchase.MotivoRefund, Status = "Required",
            }, SetOptions.MergeAll, cancellationToken);
        }

        private static string RefundStatusFromProvider(string? status) => status?.Trim().ToLowerInvariant() switch
        {
            "approved" => "Completed",
            "pending" or "in_process" => "Pending",
            _ => "Failed",
        };

        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
