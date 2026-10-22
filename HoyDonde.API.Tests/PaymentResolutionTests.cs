using System;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class PaymentResolutionTests
    {
        private static Compra PendingPurchase() => new()
        {
            Id = "purchase-1",
            ImporteTotal = 1250.50m,
            Moneda = "ARS",
            EstadoCompra = Compra.PurchaseStatus.PendingPayment,
            EstadoReserva = Compra.ReservationStatus.Active,
            ExpiresAt = new DateTime(2026, 1, 10, 12, 15, 0, DateTimeKind.Utc),
        };

        [Fact]
        public void Decide_OnlyVerifiedMatchingEarlyApprovalCanConfirm()
        {
            var purchase = PendingPurchase();
            var decision = PaymentResolution.Decide(purchase, new MercadoPagoPayment("pay-1", "approved", "purchase-1", 1250.50m, "ARS", purchase.ExpiresAt!.Value.AddSeconds(-1)));

            Assert.Equal(PaymentResolutionKind.Approve, decision.Kind);
            Assert.Equal(PaymentClassification.Approved, decision.Classification);
        }

        [Theory]
        [InlineData("pending", PaymentResolutionKind.NoChange)]
        [InlineData("rejected", PaymentResolutionKind.NotApproved)]
        [InlineData("unknown", PaymentResolutionKind.VerificationException)]
        public void Decide_NonApprovedStatusesNeverConfirm(string rawStatus, PaymentResolutionKind expected)
        {
            var decision = PaymentResolution.Decide(PendingPurchase(), new MercadoPagoPayment("pay-1", rawStatus, "purchase-1", 1250.50m, "ARS", null));

            Assert.Equal(expected, decision.Kind);
        }

        [Fact]
        public void Decide_MismatchedCorrelationOrMoneyIsVerificationException()
        {
            var purchase = PendingPurchase();
            var decision = PaymentResolution.Decide(purchase, new MercadoPagoPayment("pay-1", "approved", "another-purchase", 1m, "USD", purchase.ExpiresAt!.Value.AddMinutes(-1)));

            Assert.Equal(PaymentResolutionKind.VerificationException, decision.Kind);
        }
    }
}
