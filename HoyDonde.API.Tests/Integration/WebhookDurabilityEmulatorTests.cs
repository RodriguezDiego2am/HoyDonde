using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    [Collection(FirestoreEmulatorCollection.Name)]
    public class WebhookDurabilityEmulatorTests
    {
        private const string Secret = "emulator-webhook-secret";
        private const int TimestampToleranceSeconds = 60;
        private readonly FirestoreEmulatorFixture _fixture;
        public WebhookDurabilityEmulatorTests(FirestoreEmulatorFixture fixture) => _fixture = fixture;

        [FirestoreEmulatorFact]
        public async Task HttpWebhook_InvalidSignatureMutatesNoInboxAndValidAckFollowsDurableInboxCommit()
        {
            var invalidPaymentId = $"invalid-{Guid.NewGuid():N}";
            var validPaymentId = $"valid-{Guid.NewGuid():N}";
            using var factory = new WebhookFactory(_fixture.Db!);
            using var client = factory.CreateClient();

            var invalid = await PostAsync(client, invalidPaymentId, "ts=1,v1=not-a-valid-signature");
            var freshTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var valid = await PostAsync(client, validPaymentId, Signature(validPaymentId, "request-1", freshTimestamp));
            stopwatch.Stop();

            var invalidId = Hash(invalidPaymentId);
            var validId = Hash(validPaymentId);
            var invalidSnapshot = await _fixture.Db!.Collection("payment_webhook_inbox").Document(invalidId).GetSnapshotAsync();
            var validSnapshot = await _fixture.Db.Collection("payment_webhook_inbox").Document(validId).GetSnapshotAsync();
            var persisted = validSnapshot.ConvertTo<PaymentWebhookInboxItem>();

            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            Assert.False(invalidSnapshot.Exists);
            Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(22));
            Assert.True(validSnapshot.Exists);
            Assert.Equal(validPaymentId, persisted.ExternalPaymentId);
            Assert.Equal("Pending", persisted.ProcessingStatus);
            Assert.Equal(1, persisted.Revision);
        }

        [FirestoreEmulatorFact]
        public async Task HttpWebhook_ValidButStaleSignatureMutatesNoInbox()
        {
            var stalePaymentId = $"stale-{Guid.NewGuid():N}";
            using var factory = new WebhookFactory(_fixture.Db!);
            using var client = factory.CreateClient();
            var staleTimestamp = DateTimeOffset.UtcNow.AddSeconds(-(TimestampToleranceSeconds + 1)).ToUnixTimeSeconds().ToString();

            var stale = await PostAsync(client, stalePaymentId, Signature(stalePaymentId, "request-1", staleTimestamp));
            var staleSnapshot = await _fixture.Db!.Collection("payment_webhook_inbox").Document(Hash(stalePaymentId)).GetSnapshotAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
            Assert.False(staleSnapshot.Exists);
        }

        private static Task<HttpResponseMessage> PostAsync(HttpClient client, string paymentId, string signature)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/mercado-pago/webhook")
            {
                Content = new StringContent($"{{\"data\":{{\"id\":\"{paymentId}\"}}}}", Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("x-signature", signature);
            request.Headers.TryAddWithoutValidation("x-request-id", "request-1");
            return client.SendAsync(request);
        }

        private static string Signature(string paymentId, string requestId, string timestamp)
        {
            var manifest = $"id:{paymentId};request-id:{requestId};ts:{timestamp};";
            var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(manifest));
            return $"ts={timestamp},v1={Convert.ToHexString(digest).ToLowerInvariant()}";
        }

        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private sealed class WebhookFactory : EmulatorWebApplicationFactory
        {
            public WebhookFactory(FirestoreDb db) : base(db) { }
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MercadoPago:WebhookSecret"] = Secret,
                    ["MercadoPago:WebhookTimestampToleranceSeconds"] = TimestampToleranceSeconds.ToString(),
                }));
                // This HTTP durability test exercises only controller-to-Firestore ordering; workers
                // are tested separately with a fake provider and must not make network calls here.
                builder.ConfigureServices(services =>
                {
                    foreach (var worker in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType?.Namespace == "HoyDonde.API.Services").ToList())
                        services.Remove(worker);
                });
            }
        }
    }
}
