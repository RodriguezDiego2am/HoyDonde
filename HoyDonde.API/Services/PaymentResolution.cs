using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public enum PaymentResolutionKind { NoChange, Approve, NotApproved, VerificationException }
    public sealed record PaymentResolutionResult(PaymentResolutionKind Kind, PaymentClassification Classification, string SanitizedReason);

    // This pure gate makes the remote response prove correlation, money, currency, and status before
    // Firestore is allowed to make an irreversible reservation decision.
    public static class PaymentResolution
    {
        public static PaymentResolutionResult Decide(Compra purchase, MercadoPagoPayment payment)
        {
            var classification = MercadoPagoPaymentStatusMapper.Map(payment.RawStatus);
            if (payment.ExternalReference != purchase.Id || payment.Amount != purchase.ImporteTotal || payment.Currency != "ARS" || purchase.Moneda != "ARS")
                return new(PaymentResolutionKind.VerificationException, classification, "PaymentMismatch");

            return classification switch
            {
                PaymentClassification.Approved when payment.ApprovedAt is not null => new(PaymentResolutionKind.Approve, classification, "Approved"),
                PaymentClassification.Approved => new(PaymentResolutionKind.VerificationException, classification, "MissingApprovedAt"),
                PaymentClassification.AwaitingResolution => new(PaymentResolutionKind.NoChange, classification, "AwaitingResolution"),
                PaymentClassification.NotApprovedTerminal => new(PaymentResolutionKind.NotApproved, classification, "NotApproved"),
                PaymentClassification.ReversedOrDisputed => new(PaymentResolutionKind.VerificationException, classification, "ReversedOrDisputed"),
                _ => new(PaymentResolutionKind.VerificationException, classification, "UnknownPaymentStatus"),
            };
        }
    }
}
