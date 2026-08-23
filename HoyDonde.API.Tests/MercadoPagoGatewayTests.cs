using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HoyDonde.API.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Contract gate only: these sanitized facts document what must be validated before any
    // provider client is enabled. This slice intentionally has no Mercado Pago HTTP client.
    public class MercadoPagoGatewayTests
    {
        private static JsonElement ReadContract()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mercado-pago-contracts.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }

        [Fact]
        public void ContractFixture_RequiresCheckoutProArgentinaArsAndServerCorrelation()
        {
            var contract = ReadContract();

            Assert.Equal("Checkout Pro", contract.GetProperty("checkoutProduct").GetString());
            Assert.Equal("ARG", contract.GetProperty("country").GetString());
            Assert.Equal("ARS", contract.GetProperty("currency").GetString());
            Assert.Equal("external_reference", contract.GetProperty("serverCorrelationField").GetString());
            Assert.True(contract.GetProperty("preferenceFields").GetProperty("items").GetBoolean());
            Assert.True(contract.GetProperty("preferenceFields").GetProperty("backUrls").GetBoolean());
        }

        [Fact]
        public async Task CreatePreferenceAsync_IncludesConfiguredBackUrlsForCheckoutReturnRouting()
        {
            string? payload = null;
            var handler = new StubHandler(async request =>
            {
                payload = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\":\"pref-1\",\"init_point\":\"https://checkout.example/pref-1\"}")
                };
            });
            var gateway = new MercadoPagoGateway(new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") }, Options.Create(new MercadoPagoOptions
            {
                CheckoutSuccessUrl = "hoydonde://purchases/success",
                CheckoutFailureUrl = "hoydonde://purchases/failure",
                CheckoutPendingUrl = "hoydonde://purchases/pending",
            }));

            await gateway.CreatePreferenceAsync(new MercadoPagoPreferenceRequest("purchase-1", "General", 2, 1200m), CancellationToken.None);

            using var document = JsonDocument.Parse(payload!);
            var backUrls = document.RootElement.GetProperty("back_urls");
            Assert.Equal("hoydonde://purchases/success", backUrls.GetProperty("success").GetString());
            Assert.Equal("hoydonde://purchases/failure", backUrls.GetProperty("failure").GetString());
            Assert.Equal("hoydonde://purchases/pending", backUrls.GetProperty("pending").GetString());
        }

        [Fact]
        public void ContractFixture_RecordsVerificationBoundariesAndNoBlindRetryPolicy()
        {
            var contract = ReadContract();

            Assert.Equal("GET /v1/payments/{id}", contract.GetProperty("paymentSourceOfTruth").GetString());
            Assert.Equal("approvedAt", contract.GetProperty("authoritativeApprovalTime").GetString());
            Assert.Equal("x-signature", contract.GetProperty("webhook").GetProperty("signatureHeader").GetString());
            Assert.Equal("HMAC-SHA256", contract.GetProperty("webhook").GetProperty("algorithm").GetString());
            Assert.Equal("POST /v1/payments/{id}/refunds", contract.GetProperty("totalRefund").GetProperty("endpoint").GetString());
            Assert.True(contract.GetProperty("totalRefund").GetProperty("omitsAmount").GetBoolean());
            Assert.False(contract.GetProperty("preferenceCreation").GetProperty("idempotencyVerified").GetBoolean());
            Assert.True(contract.GetProperty("preferenceCreation").GetProperty("noBlindRetry").GetBoolean());
            Assert.False(contract.GetProperty("rolloutAllowed").GetBoolean());
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
            public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request);
        }
    }
}
