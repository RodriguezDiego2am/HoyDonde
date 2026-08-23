using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    [Collection(FirestoreEmulatorCollection.Name)]
    public class PurchaseServiceEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public PurchaseServiceEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<(string eventId, string ticketTypeId)> SeedEventAsync(int stock)
        {
            var eventId = $"purchase-event-{Guid.NewGuid():N}";
            var ticketTypeId = $"purchase-type-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Reserva de prueba",
                Ubicacion = "Buenos Aires",
                FechaInicio = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc),
                FechaFin = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc),
                Estado = Event.EventStatus.Publicado,
                TicketTypes = new List<TicketType>
                {
                    new() { Id = ticketTypeId, Nombre = "General", Precio = 1500m, CantidadDisponible = stock, EventoId = eventId }
                }
            });
            return (eventId, ticketTypeId);
        }

        private static PurchaseReservationRequest RequestFor(string eventId, string ticketTypeId, int quantity) => new()
        {
            ClientePersonaId = "persona-test",
            EventoId = eventId,
            TicketTypeId = ticketTypeId,
            Cantidad = quantity,
        };

        [FirestoreEmulatorFact]
        public async Task ReserveAsync_CreatesAnActiveFifteenMinuteReservationAndDeductsStock()
        {
            var sut = new PurchaseService(_fixture.Db!);
            var (eventId, ticketTypeId) = await SeedEventAsync(stock: 2);
            var reservedAt = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

            var compra = await sut.ReserveAsync(RequestFor(eventId, ticketTypeId, 2), reservedAt);
            var persistedEvent = (await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync()).ConvertTo<Event>();

            Assert.Equal(Compra.PurchaseStatus.PendingPayment, compra.EstadoCompra);
            Assert.Equal(Compra.ReservationStatus.Active, compra.EstadoReserva);
            Assert.Equal(reservedAt.AddMinutes(15), compra.ExpiresAt);
            Assert.Equal(0, persistedEvent.TicketTypes.Single().CantidadDisponible);
            Assert.Empty((await _fixture.Db.Collection("tickets").GetSnapshotAsync()).Documents.Where(d => d.ConvertTo<Ticket>().CompraId == compra.Id));
        }

        [FirestoreEmulatorFact]
        public async Task ReserveAsync_ConcurrentLastStock_AllowsExactlyOneReservation()
        {
            var (eventId, ticketTypeId) = await SeedEventAsync(stock: 1);
            var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            var first = new PurchaseService(_fixture.Db!);
            var second = new PurchaseService(_fixture.Db!);

            async Task<bool> TryReserveAsync(PurchaseService service)
            {
                try
                {
                    await service.ReserveAsync(RequestFor(eventId, ticketTypeId, 1), now);
                    return true;
                }
                catch (StockInsuficienteException)
                {
                    return false;
                }
            }

            var results = await Task.WhenAll(TryReserveAsync(first), TryReserveAsync(second));
            var persistedEvent = (await _fixture.Db!.Collection("events").Document(eventId).GetSnapshotAsync()).ConvertTo<Event>();
            var purchases = await _fixture.Db.Collection("compras").GetSnapshotAsync();

            Assert.Equal(1, results.Count(result => result));
            Assert.Equal(0, persistedEvent.TicketTypes.Single().CantidadDisponible);
            Assert.Single(purchases.Documents.Where(d => d.ConvertTo<Compra>().EventoId == eventId));
        }

        [FirestoreEmulatorFact]
        public async Task ExpiryWinsThenConcurrentApprovals_AreDeterministicallyRefundOnlyWithoutTickets()
        {
            var sut = new PurchaseService(_fixture.Db!);
            var (eventId, ticketTypeId) = await SeedEventAsync(stock: 2);
            var reservedAt = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            var compra = await sut.ReserveAsync(RequestFor(eventId, ticketTypeId, 2), reservedAt);

            // The explicit expiry winner makes the race repeatable: released local stock is authoritative.
            Assert.Equal(PurchaseTransition.Expired, await sut.ExpireAsync(compra.Id, compra.ExpiresAt!.Value));
            await Task.WhenAll(
                sut.ProcessVerifiedApprovalAsync(compra.Id, reservedAt.AddMinutes(14)),
                sut.ProcessVerifiedApprovalAsync(compra.Id, reservedAt.AddMinutes(14)));

            var persisted = (await _fixture.Db.Collection("compras").Document(compra.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            var tickets = (await _fixture.Db.Collection("tickets").GetSnapshotAsync()).Documents
                .Select(d => d.ConvertTo<Ticket>()).Where(ticket => ticket.CompraId == compra.Id).ToList();
            var persistedEvent = (await _fixture.Db.Collection("events").Document(eventId).GetSnapshotAsync()).ConvertTo<Event>();

            Assert.Equal(Compra.PurchaseStatus.RefundRequired, persisted.EstadoCompra);
            Assert.Equal(Compra.ReservationStatus.Released, persisted.EstadoReserva);
            Assert.Equal(Compra.RefundReason.ReservationReleasedBeforeVerification, persisted.MotivoRefund);
            Assert.Empty(tickets);
            Assert.Equal(2, persistedEvent.TicketTypes.Single().CantidadDisponible);
        }

        [FirestoreEmulatorFact]
        public async Task WebhookInbox_LaterNotificationAfterProcessed_RequeuesTheSameDeterministicRecord()
        {
            var paymentId = $"payment-{Guid.NewGuid():N}";
            var inbox = new PaymentWebhookInbox(_fixture.Db!);
            var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paymentId))).ToLowerInvariant();
            var reference = _fixture.Db!.Collection("payment_webhook_inbox").Document(id);

            await inbox.PersistAsync(paymentId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
            await reference.SetAsync(new { ProcessingStatus = "Processed" }, Google.Cloud.Firestore.SetOptions.MergeAll);
            await inbox.PersistAsync(paymentId, new DateTime(2026, 1, 10, 12, 1, 0, DateTimeKind.Utc));

            var persisted = (await reference.GetSnapshotAsync()).ConvertTo<PaymentWebhookInboxItem>();
            Assert.Equal(paymentId, persisted.ExternalPaymentId);
            Assert.Equal("Pending", persisted.ProcessingStatus);
            Assert.Equal(2, persisted.Revision);
        }

        [FirestoreEmulatorFact]
        public async Task ApprovalAtExpiryAndApprovalAfterRelease_AreRefundOnlyAndNeverEmitTickets()
        {
            var sut = new PurchaseService(_fixture.Db!);
            var (eventId, ticketTypeId) = await SeedEventAsync(stock: 2);
            var reservedAt = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            var atBoundary = await sut.ReserveAsync(RequestFor(eventId, ticketTypeId, 1), reservedAt);
            var releasedFirst = await sut.ReserveAsync(RequestFor(eventId, ticketTypeId, 1), reservedAt);

            await sut.ProcessVerifiedApprovalAsync(atBoundary.Id, atBoundary.ExpiresAt!.Value);
            await sut.ExpireAsync(releasedFirst.Id, releasedFirst.ExpiresAt!.Value);
            await sut.ProcessVerifiedApprovalAsync(releasedFirst.Id, reservedAt.AddMinutes(14));

            var boundary = (await _fixture.Db.Collection("compras").Document(atBoundary.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            var lateProcessed = (await _fixture.Db.Collection("compras").Document(releasedFirst.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            var tickets = (await _fixture.Db.Collection("tickets").GetSnapshotAsync()).Documents
                .Select(d => d.ConvertTo<Ticket>()).Where(ticket => ticket.CompraId == atBoundary.Id || ticket.CompraId == releasedFirst.Id).ToList();

            Assert.Equal(Compra.PurchaseStatus.RefundRequired, boundary.EstadoCompra);
            Assert.Equal(Compra.RefundReason.ApprovalAtOrAfterReservationExpiry, boundary.MotivoRefund);
            Assert.Equal(Compra.PurchaseStatus.RefundRequired, lateProcessed.EstadoCompra);
            Assert.Equal(Compra.RefundReason.ReservationReleasedBeforeVerification, lateProcessed.MotivoRefund);
            Assert.Empty(tickets);
        }
    }
}
