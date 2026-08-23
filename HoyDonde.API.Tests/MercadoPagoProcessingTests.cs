using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HoyDonde.API.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class MercadoPagoProcessingTests
    {
        [Fact]
        public void SignatureVerifier_AcceptsOnlyTheOfficialPaymentManifest()
        {
            const string secret = "test-webhook-secret";
            const string paymentId = "12345";
            const string requestId = "request-9";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var manifest = $"id:{paymentId};request-id:{requestId};ts:{timestamp};";
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
            var verifier = new MercadoPagoSignatureVerifier(Options.Create(new MercadoPagoOptions { WebhookSecret = secret }));

            Assert.True(verifier.IsValid(paymentId, $"ts={timestamp},v1={signature}", requestId));
            Assert.False(verifier.IsValid(paymentId, $"ts={timestamp},v1={signature[..^1]}0", requestId));
            Assert.False(verifier.IsValid(paymentId, $"ts={timestamp},v1={signature}", string.Empty));
        }

        [Fact]
        public async Task Gateway_GetPaymentAndFullRefund_UsesProviderTruthAndStableIdempotencyWithoutAmount()
        {
            HttpRequestMessage? observed = null;
            using var client = new HttpClient(new StubHandler(request =>
            {
                observed = request;
                return request.Method == HttpMethod.Get
                    ? Json("{\"id\":\"payment-1\",\"status\":\"approved\",\"external_reference\":\"purchase-1\",\"transaction_amount\":1250.50,\"currency_id\":\"ARS\",\"date_approved\":\"2026-01-10T12:00:00Z\"}")
                    : Json("{\"id\":\"refund-1\",\"status\":\"approved\"}");
            })) { BaseAddress = new Uri("https://api.mercadopago.test/") };
            var gateway = new MercadoPagoGateway(client, Options.Create(new MercadoPagoOptions { AccessToken = "never-log-this" }));

            var payment = await gateway.GetPaymentAsync("payment-1", CancellationToken.None);
            Assert.Equal("approved", payment.RawStatus);
            Assert.Equal("purchase-1", payment.ExternalReference);
            Assert.Equal("ARS", payment.Currency);

            var refund = await gateway.RefundFullAsync("payment-1", CancellationToken.None);
            Assert.Equal("refund-1", refund.ExternalRefundId);
            Assert.Equal(HttpMethod.Post, observed!.Method);
            Assert.Equal("/v1/payments/payment-1/refunds", observed.RequestUri!.AbsolutePath);
            Assert.True(observed.Headers.TryGetValues("X-Idempotency-Key", out var keys));
            Assert.Equal(MercadoPagoGateway.RefundIdempotencyKey("payment-1"), Assert.Single(keys));
            Assert.Null(observed.Content);
        }

        [Theory]
        [InlineData("approved", PaymentClassification.Approved)]
        [InlineData("pending", PaymentClassification.AwaitingResolution)]
        [InlineData("in_process", PaymentClassification.AwaitingResolution)]
        [InlineData("rejected", PaymentClassification.NotApprovedTerminal)]
        [InlineData("charged_back", PaymentClassification.ReversedOrDisputed)]
        [InlineData("unrecognized", PaymentClassification.Unknown)]
        public void PaymentStatusMapper_NormalizesEveryProviderOutcome(string rawStatus, PaymentClassification expected)
        {
            Assert.Equal(expected, MercadoPagoPaymentStatusMapper.Map(rawStatus));
        }

        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_response(request));
        }
    }
}
