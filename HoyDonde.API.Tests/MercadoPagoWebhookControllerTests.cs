using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HoyDonde.API.Controllers;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class MercadoPagoWebhookControllerTests
    {
        private const string Secret = "webhook-test-secret";

        [Fact]
        public async Task Receive_InvalidSignature_RejectsBeforeInboxMutation()
        {
            var inbox = new Mock<IPaymentWebhookInbox>();
            var sut = Create(inbox.Object);

            var result = await sut.Receive(new MercadoPagoWebhookRequest { Data = new MercadoPagoWebhookData { Id = "pay-1" } }, "ts=1,v1=bad", "request-1");

            Assert.IsType<UnauthorizedResult>(result);
            inbox.Verify(x => x.PersistAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task Receive_InboxFailure_Returns5xxSoProviderCanRetry()
        {
            var inbox = new Mock<IPaymentWebhookInbox>();
            inbox.Setup(x => x.PersistAsync("pay-1", It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("persistence unavailable"));
            var sut = Create(inbox.Object);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var result = await sut.Receive(new MercadoPagoWebhookRequest { Data = new MercadoPagoWebhookData { Id = "pay-1" } }, Signature("pay-1", "request-1", timestamp), "request-1");

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
        }

        [Fact]
        public async Task Receive_ValidNotification_PersistsInboxBeforeReturningCreatedAck()
        {
            var inbox = new Mock<IPaymentWebhookInbox>();
            var sut = Create(inbox.Object);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var result = await sut.Receive(new MercadoPagoWebhookRequest { Data = new MercadoPagoWebhookData { Id = "pay-1" } }, Signature("pay-1", "request-1", timestamp), "request-1");

            Assert.IsType<CreatedResult>(result);
            inbox.Verify(x => x.PersistAsync("pay-1", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Receive_ValidButStaleSignature_RejectsBeforeInboxMutation()
        {
            var inbox = new Mock<IPaymentWebhookInbox>();
            var sut = Create(inbox.Object, toleranceSeconds: 60);
            var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds().ToString();

            var result = await sut.Receive(new MercadoPagoWebhookRequest { Data = new MercadoPagoWebhookData { Id = "pay-1" } }, Signature("pay-1", "request-1", staleTimestamp), "request-1");

            Assert.IsType<UnauthorizedResult>(result);
            inbox.Verify(x => x.PersistAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        private static MercadoPagoWebhookController Create(IPaymentWebhookInbox inbox, int toleranceSeconds = 300) => new(
            new MercadoPagoSignatureVerifier(Options.Create(new MercadoPagoOptions { WebhookSecret = Secret, WebhookTimestampToleranceSeconds = toleranceSeconds })),
            inbox,
            NullLogger<MercadoPagoWebhookController>.Instance);

        private static string Signature(string paymentId, string requestId, string timestamp)
        {
            var manifest = $"id:{paymentId};request-id:{requestId};ts:{timestamp};";
            var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(manifest));
            return $"ts={timestamp},v1={Convert.ToHexString(digest).ToLowerInvariant()}";
        }
    }
}
