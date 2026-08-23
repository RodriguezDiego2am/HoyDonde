using System;

namespace HoyDonde.API.Models
{
    // Groups tickets emitted by one purchase. Ticket.CompraId remains the sole relationship.
    [Google.Cloud.Firestore.FirestoreData]
    public class Compra
    {
        // Additive defaults preserve documents from the simulated-payment MVP without a backfill.
        public enum PurchaseStatus
        {
            LegacySimulated,
            PendingPayment,
            VerificationException,
            PaymentNotApproved,
            Expired,
            RefundRequired,
            RefundInProgress,
            RefundPending,
            RefundFailed,
            RefundCompleted,
            Confirmed
        }

        public enum ReservationStatus
        {
            None,
            Active,
            Consumed,
            Released
        }

        public enum RefundReason
        {
            None,
            ApprovalAtOrAfterReservationExpiry,
            ReservationReleasedBeforeVerification
        }

        public enum CheckoutStatus
        {
            NotStarted,
            Creating,
            Ready,
            CreationUnknown,
            Failed
        }

        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ClientePersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaCompra { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public int CantidadEntradas { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty(ConverterType = typeof(HoyDonde.API.Converters.DecimalFirestoreConverter))]
        public decimal ImporteTotal { get; set; }

        // Existing reads continue to identify historic simulated purchases with this field.
        [Google.Cloud.Firestore.FirestoreProperty]
        public bool PagoSimulado { get; set; } = true;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoNombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Ubicacion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaInicio { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaFin { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string OrganizadorPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public Event.EventCategory? Categoria { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public int SchemaVersion { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public PurchaseStatus EstadoCompra { get; set; } = PurchaseStatus.LegacySimulated;

        [Google.Cloud.Firestore.FirestoreProperty]
        public ReservationStatus EstadoReserva { get; set; } = ReservationStatus.None;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? ReservedAt { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? ExpiresAt { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TicketTypeId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TicketTypeNombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Moneda { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public RefundReason MotivoRefund { get; set; } = RefundReason.None;

        [Google.Cloud.Firestore.FirestoreProperty]
        public long TransitionVersion { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public CheckoutStatus EstadoCheckout { get; set; } = CheckoutStatus.NotStarted;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string? CheckoutPreferenceId { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string? CheckoutUrl { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string? PurchaseIdempotencyKeyHash { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string? RefundStatus { get; set; }
    }
}
