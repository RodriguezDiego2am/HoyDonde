using System;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class PurchaseStateMachineTests
    {
        private static readonly DateTime ReservedAt = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void CreatePending_UsesAnExactFifteenMinuteActiveReservation()
        {
            var compra = PurchaseStateMachine.CreatePending(ReservedAt);

            Assert.Equal(Compra.PurchaseStatus.PendingPayment, compra.EstadoCompra);
            Assert.Equal(Compra.ReservationStatus.Active, compra.EstadoReserva);
            Assert.Equal(ReservedAt, compra.ReservedAt);
            Assert.Equal(ReservedAt.AddMinutes(15), compra.ExpiresAt);
            Assert.False(PurchaseStateMachine.CanEmitTickets(compra));
        }

        [Fact]
        public void ApplyVerifiedApproval_StrictlyBeforeExpiry_ConsumesAndConfirms()
        {
            var compra = PurchaseStateMachine.CreatePending(ReservedAt);

            var result = PurchaseStateMachine.ApplyVerifiedApproval(compra, ReservedAt.AddMinutes(14).AddSeconds(59));

            Assert.Equal(PurchaseTransition.Confirmed, result);
            Assert.Equal(Compra.PurchaseStatus.Confirmed, compra.EstadoCompra);
            Assert.Equal(Compra.ReservationStatus.Consumed, compra.EstadoReserva);
            Assert.True(PurchaseStateMachine.CanEmitTickets(compra));
        }

        [Fact]
        public void ApplyVerifiedApproval_AtExactExpiry_ReleasesAndRequiresRefund()
        {
            var compra = PurchaseStateMachine.CreatePending(ReservedAt);

            var result = PurchaseStateMachine.ApplyVerifiedApproval(compra, compra.ExpiresAt!.Value);

            Assert.Equal(PurchaseTransition.RefundRequired, result);
            Assert.Equal(Compra.PurchaseStatus.RefundRequired, compra.EstadoCompra);
            Assert.Equal(Compra.ReservationStatus.Released, compra.EstadoReserva);
            Assert.Equal(Compra.RefundReason.ApprovalAtOrAfterReservationExpiry, compra.MotivoRefund);
            Assert.False(PurchaseStateMachine.CanEmitTickets(compra));
        }

        [Fact]
        public void ApplyVerifiedApproval_AfterLocalRelease_IsRefundOnlyEvenWhenApprovalWasEarly()
        {
            var compra = PurchaseStateMachine.CreatePending(ReservedAt);
            Assert.True(PurchaseStateMachine.Expire(compra, compra.ExpiresAt!.Value));

            var result = PurchaseStateMachine.ApplyVerifiedApproval(compra, ReservedAt.AddMinutes(14));

            Assert.Equal(PurchaseTransition.RefundRequired, result);
            Assert.Equal(Compra.PurchaseStatus.RefundRequired, compra.EstadoCompra);
            Assert.Equal(Compra.ReservationStatus.Released, compra.EstadoReserva);
            Assert.Equal(Compra.RefundReason.ReservationReleasedBeforeVerification, compra.MotivoRefund);
            Assert.False(PurchaseStateMachine.CanEmitTickets(compra));
        }

        [Fact]
        public void Expire_AtBoundaryIsIdempotentAndNeverReleasesACapturedReservation()
        {
            var expired = PurchaseStateMachine.CreatePending(ReservedAt);
            var confirmed = PurchaseStateMachine.CreatePending(ReservedAt);
            PurchaseStateMachine.ApplyVerifiedApproval(confirmed, ReservedAt.AddMinutes(1));

            Assert.True(PurchaseStateMachine.Expire(expired, expired.ExpiresAt!.Value));
            Assert.False(PurchaseStateMachine.Expire(expired, expired.ExpiresAt!.Value.AddMinutes(1)));
            Assert.False(PurchaseStateMachine.Expire(confirmed, confirmed.ExpiresAt!.Value.AddMinutes(1)));
            Assert.Equal(Compra.ReservationStatus.Released, expired.EstadoReserva);
            Assert.Equal(Compra.ReservationStatus.Consumed, confirmed.EstadoReserva);
        }
    }
}
