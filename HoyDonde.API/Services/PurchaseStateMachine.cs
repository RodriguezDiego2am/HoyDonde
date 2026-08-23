using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public enum PurchaseTransition
    {
        NoChange,
        Confirmed,
        Expired,
        RefundRequired
    }

    // Pure transition rules. PurchaseService applies the resulting mutation inside Firestore
    // transactions, making Active -> Consumed or Active -> Released the only competing decision.
    public static class PurchaseStateMachine
    {
        public static readonly TimeSpan ReservationDuration = TimeSpan.FromMinutes(15);

        public static Compra CreatePending(DateTime reservedAt)
        {
            var utcReservedAt = ToUtc(reservedAt);
            return new Compra
            {
                SchemaVersion = 1,
                PagoSimulado = false,
                EstadoCompra = Compra.PurchaseStatus.PendingPayment,
                EstadoReserva = Compra.ReservationStatus.Active,
                ReservedAt = utcReservedAt,
                ExpiresAt = utcReservedAt.Add(ReservationDuration),
            };
        }

        public static PurchaseTransition ApplyVerifiedApproval(Compra compra, DateTime approvedAt)
        {
            ArgumentNullException.ThrowIfNull(compra);

            if (compra.EstadoCompra == Compra.PurchaseStatus.Confirmed)
                return PurchaseTransition.NoChange;

            if (compra.EstadoReserva == Compra.ReservationStatus.Released)
                return RequireRefund(compra, Compra.RefundReason.ReservationReleasedBeforeVerification);

            if (compra.EstadoReserva != Compra.ReservationStatus.Active || compra.ExpiresAt is null)
                return PurchaseTransition.NoChange;

            if (ToUtc(approvedAt) >= compra.ExpiresAt.Value)
            {
                compra.EstadoReserva = Compra.ReservationStatus.Released;
                compra.EstadoCompra = Compra.PurchaseStatus.RefundRequired;
                compra.MotivoRefund = Compra.RefundReason.ApprovalAtOrAfterReservationExpiry;
                compra.TransitionVersion++;
                return PurchaseTransition.RefundRequired;
            }

            compra.EstadoReserva = Compra.ReservationStatus.Consumed;
            compra.EstadoCompra = Compra.PurchaseStatus.Confirmed;
            compra.MotivoRefund = Compra.RefundReason.None;
            compra.TransitionVersion++;
            return PurchaseTransition.Confirmed;
        }

        public static bool ApplyNotApproved(Compra compra)
        {
            ArgumentNullException.ThrowIfNull(compra);
            if (compra.EstadoReserva != Compra.ReservationStatus.Active) return false;
            compra.EstadoReserva = Compra.ReservationStatus.Released;
            compra.EstadoCompra = Compra.PurchaseStatus.PaymentNotApproved;
            compra.TransitionVersion++;
            return true;
        }

        public static bool Expire(Compra compra, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(compra);
            if (compra.EstadoReserva != Compra.ReservationStatus.Active || compra.ExpiresAt is null || ToUtc(utcNow) < compra.ExpiresAt.Value)
                return false;

            compra.EstadoReserva = Compra.ReservationStatus.Released;
            compra.EstadoCompra = Compra.PurchaseStatus.Expired;
            compra.TransitionVersion++;
            return true;
        }

        public static bool CanEmitTickets(Compra compra) =>
            compra.EstadoCompra == Compra.PurchaseStatus.Confirmed &&
            compra.EstadoReserva == Compra.ReservationStatus.Consumed;

        private static PurchaseTransition RequireRefund(Compra compra, Compra.RefundReason reason)
        {
            if (compra.EstadoCompra == Compra.PurchaseStatus.RefundRequired)
                return PurchaseTransition.NoChange;

            compra.EstadoCompra = Compra.PurchaseStatus.RefundRequired;
            compra.MotivoRefund = reason;
            compra.TransitionVersion++;
            return PurchaseTransition.RefundRequired;
        }

        private static DateTime ToUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }
}
