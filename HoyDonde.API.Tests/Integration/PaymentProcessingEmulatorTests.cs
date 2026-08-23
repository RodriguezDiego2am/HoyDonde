using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    [Collection(FirestoreEmulatorCollection.Name)]
    public class PaymentProcessingEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;
        public PaymentProcessingEmulatorTests(FirestoreEmulatorFixture fixture) => _fixture = fixture;

        [FirestoreEmulatorFact]
        public async Task InboxWorker_RequeuedLaterNotificationUsesPaymentGetAsTheOnlyDecisionSource()
        {
            var purchase = await ReserveAsync();
            var paymentId = $"payment-{Guid.NewGuid():N}";
            var gateway = new RecordingGateway(
                new MercadoPagoPayment(paymentId, "pending", purchase.Id, purchase.ImporteTotal, "ARS", null),
                new MercadoPagoPayment(paymentId, "approved", purchase.Id, purchase.ImporteTotal, "ARS", purchase.ExpiresAt!.Value.AddSeconds(-1)));
            var inbox = new PaymentWebhookInbox(_fixture.Db!);
            var sut = new PaymentProcessingService(_fixture.Db!, gateway, new PurchaseService(_fixture.Db!), NullLogger<PaymentProcessingService>.Instance);

            await inbox.PersistAsync(paymentId, DateTime.UtcNow);
            await sut.ProcessPendingInboxAsync(CancellationToken.None);
            await inbox.PersistAsync(paymentId, DateTime.UtcNow.AddSeconds(1));
            await sut.ProcessPendingInboxAsync(CancellationToken.None);

            var persisted = (await _fixture.Db!.Collection("compras").Document(purchase.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            var tickets = (await _fixture.Db.Collection("tickets").GetSnapshotAsync()).Documents
                .Select(document => document.ConvertTo<Ticket>()).Where(ticket => ticket.CompraId == purchase.Id).ToList();
            Assert.Equal(2, gateway.PaymentGetCalls);
            Assert.Equal(Compra.PurchaseStatus.Confirmed, persisted.EstadoCompra);
            Assert.Single(tickets);
        }

        [FirestoreEmulatorFact]
        public async Task InboxWorker_LaterRevisionDuringClaimRemainsPendingAfterStaleCompletion()
        {
            var paymentId = $"payment-{Guid.NewGuid():N}";
            var db = _fixture.Db!;
            var inbox = new PaymentWebhookInbox(db);
            var gateway = new BlockingPaymentGateway(new MercadoPagoPayment(paymentId, "pending", string.Empty, 0m, "ARS", null));
            var sut = new PaymentProcessingService(db, gateway, new PurchaseService(db), NullLogger<PaymentProcessingService>.Instance);

            await inbox.PersistAsync(paymentId, DateTime.UtcNow);
            var processing = sut.ProcessPendingInboxAsync(CancellationToken.None);
            await gateway.WaitForFirstGetAsync();
            await inbox.PersistAsync(paymentId, DateTime.UtcNow.AddSeconds(1));
            gateway.CompleteFirstGet();
            await processing;

            var snapshot = await db.Collection("payment_webhook_inbox").WhereEqualTo("ExternalPaymentId", paymentId).GetSnapshotAsync();
            var persisted = Assert.Single(snapshot.Documents).ConvertTo<PaymentWebhookInboxItem>();
            Assert.Equal(1, gateway.PaymentGetCalls);
            Assert.Equal(2, persisted.Revision);
            Assert.Equal("Pending", persisted.ProcessingStatus);
            Assert.Null(persisted.ProcessingRevision);
        }

        [FirestoreEmulatorFact]
        public async Task RefundWorker_SuccessfulProviderRefundCompletesJobAndPurchase()
        {
            var purchase = await ReserveAsync();
            var paymentId = $"payment-{Guid.NewGuid():N}";
            var paymentRecordId = $"record-{Guid.NewGuid():N}";
            var jobId = $"job-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("compras").Document(purchase.Id).SetAsync(new
            {
                EstadoCompra = Compra.PurchaseStatus.RefundRequired,
                RefundStatus = "Required",
            }, SetOptions.MergeAll);
            await _fixture.Db.Collection("payment_records").Document(paymentRecordId).SetAsync(new PaymentRecord
            {
                Id = paymentRecordId, CompraId = purchase.Id, ExternalPaymentId = paymentId,
            });
            await _fixture.Db.Collection("refund_jobs").Document(jobId).SetAsync(new RefundJob
            {
                Id = jobId, CompraId = purchase.Id, PaymentRecordId = paymentRecordId, Status = "Required",
            });
            var gateway = new RecordingGateway(refunds: new[] { new MercadoPagoRefund("refund-1", "approved") });
            var sut = new PaymentProcessingService(_fixture.Db, gateway, new PurchaseService(_fixture.Db), NullLogger<PaymentProcessingService>.Instance);

            await sut.ProcessRequiredRefundsAsync(CancellationToken.None);

            var job = (await _fixture.Db.Collection("refund_jobs").Document(jobId).GetSnapshotAsync()).ConvertTo<RefundJob>();
            var persisted = (await _fixture.Db.Collection("compras").Document(purchase.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            Assert.Equal(1, gateway.RefundCalls);
            Assert.Equal("Completed", job.Status);
            Assert.Equal(Compra.PurchaseStatus.RefundCompleted, persisted.EstadoCompra);
            Assert.Equal("Completed", persisted.RefundStatus);
        }

        [FirestoreEmulatorFact]
        public async Task RefundWorker_RetriesPendingWithTheSameKeyAndLeavesProviderFailureTerminalAndObservable()
        {
            var purchase = await ReserveAsync();
            var paymentId = $"payment-{Guid.NewGuid():N}";
            var paymentRecordId = $"record-{Guid.NewGuid():N}";
            var jobId = $"job-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("compras").Document(purchase.Id).SetAsync(new
            {
                EstadoCompra = Compra.PurchaseStatus.RefundRequired,
                RefundStatus = "Required",
            }, SetOptions.MergeAll);
            await _fixture.Db.Collection("payment_records").Document(paymentRecordId).SetAsync(new PaymentRecord
            {
                Id = paymentRecordId, CompraId = purchase.Id, ExternalPaymentId = paymentId,
            });
            await _fixture.Db.Collection("refund_jobs").Document(jobId).SetAsync(new RefundJob
            {
                Id = jobId, CompraId = purchase.Id, PaymentRecordId = paymentRecordId, Status = "Required",
            });
            var gateway = new RecordingGateway(refunds: new[]
            {
                new MercadoPagoRefund("refund-1", "pending"),
                new MercadoPagoRefund("refund-1", "rejected"),
            });
            var sut = new PaymentProcessingService(_fixture.Db, gateway, new PurchaseService(_fixture.Db), NullLogger<PaymentProcessingService>.Instance);

            await sut.ProcessRequiredRefundsAsync(CancellationToken.None);
            await sut.ProcessRequiredRefundsAsync(CancellationToken.None);
            await sut.ProcessRequiredRefundsAsync(CancellationToken.None);

            var job = (await _fixture.Db.Collection("refund_jobs").Document(jobId).GetSnapshotAsync()).ConvertTo<RefundJob>();
            var persisted = (await _fixture.Db.Collection("compras").Document(purchase.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            Assert.Equal(2, gateway.RefundCalls);
            Assert.All(gateway.RefundKeys, key => Assert.Equal(MercadoPagoGateway.RefundIdempotencyKey(paymentId), key));
            Assert.Equal("Failed", job.Status);
            Assert.Equal(Compra.PurchaseStatus.RefundFailed, persisted.EstadoCompra);
            Assert.Equal("Failed", persisted.RefundStatus);
        }

        private async Task<Compra> ReserveAsync()
        {
            var eventId = $"event-{Guid.NewGuid():N}";
            var typeId = $"type-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId, Nombre = "Evento", Ubicacion = "Buenos Aires",
                FechaInicio = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc),
                FechaFin = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc), Estado = Event.EventStatus.Publicado,
                TicketTypes = new List<TicketType> { new() { Id = typeId, Nombre = "General", Precio = 100m, CantidadDisponible = 3, EventoId = eventId } },
            });
            return await new PurchaseService(_fixture.Db).ReserveAsync(new PurchaseReservationRequest
            {
                ClientePersonaId = "customer", EventoId = eventId, TicketTypeId = typeId, Cantidad = 1,
            }, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        }

        private sealed class BlockingPaymentGateway : IMercadoPagoGateway
        {
            private readonly MercadoPagoPayment _payment;
            private readonly TaskCompletionSource<MercadoPagoPayment> _firstGet = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _firstGetStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int PaymentGetCalls { get; private set; }

            public BlockingPaymentGateway(MercadoPagoPayment payment) => _payment = payment;
            public Task<MercadoPagoPreference> CreatePreferenceAsync(MercadoPagoPreferenceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<MercadoPagoRefund> RefundFullAsync(string paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<MercadoPagoPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
            {
                PaymentGetCalls++;
                if (PaymentGetCalls == 1)
                {
                    _firstGetStarted.TrySetResult(true);
                    return _firstGet.Task;
                }
                return Task.FromResult(_payment);
            }
            public Task WaitForFirstGetAsync() => _firstGetStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            public void CompleteFirstGet() => _firstGet.TrySetResult(_payment);
        }

        private sealed class RecordingGateway : IMercadoPagoGateway
        {
            private readonly Queue<MercadoPagoPayment> _payments;
            private readonly Queue<MercadoPagoRefund> _refunds;
            public int PaymentGetCalls { get; private set; }
            public int RefundCalls { get; private set; }
            public List<string> RefundKeys { get; } = new();
            public RecordingGateway(params MercadoPagoPayment[] payments) : this(payments, Array.Empty<MercadoPagoRefund>()) { }
            public RecordingGateway(IEnumerable<MercadoPagoPayment>? payments = null, IEnumerable<MercadoPagoRefund>? refunds = null)
            {
                _payments = new Queue<MercadoPagoPayment>(payments ?? Array.Empty<MercadoPagoPayment>());
                _refunds = new Queue<MercadoPagoRefund>(refunds ?? Array.Empty<MercadoPagoRefund>());
            }
            public Task<MercadoPagoPreference> CreatePreferenceAsync(MercadoPagoPreferenceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<MercadoPagoPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
            {
                PaymentGetCalls++;
                return Task.FromResult(_payments.Dequeue());
            }
            public Task<MercadoPagoRefund> RefundFullAsync(string paymentId, CancellationToken cancellationToken)
            {
                RefundCalls++;
                RefundKeys.Add(MercadoPagoGateway.RefundIdempotencyKey(paymentId));
                return Task.FromResult(_refunds.Dequeue());
            }
        }
    }
}
