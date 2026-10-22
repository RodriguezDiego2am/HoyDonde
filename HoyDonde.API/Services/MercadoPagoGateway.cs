using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace HoyDonde.API.Services
{
    public sealed class MercadoPagoOptions
    {
        public const string SectionName = "MercadoPago";
        public bool Enabled { get; init; }
        public string AccessToken { get; init; } = string.Empty;
        public string WebhookSecret { get; init; } = string.Empty;
        public int WebhookTimestampToleranceSeconds { get; init; } = 300;
        public string CheckoutSuccessUrl { get; init; } = string.Empty;
        public string CheckoutFailureUrl { get; init; } = string.Empty;
        public string CheckoutPendingUrl { get; init; } = string.Empty;
        public string ApiBaseUrl { get; init; } = "https://api.mercadopago.com/";
    }

    public enum PaymentClassification { Unknown, AwaitingResolution, Approved, NotApprovedTerminal, ReversedOrDisputed }

    public sealed record MercadoPagoPayment(
        string ExternalPaymentId,
        string RawStatus,
        string ExternalReference,
        decimal Amount,
        string Currency,
        DateTime? ApprovedAt);

    public sealed record MercadoPagoRefund(string ExternalRefundId, string Status);
    public sealed record MercadoPagoPreference(string PreferenceId, string CheckoutUrl);
    public sealed record MercadoPagoPreferenceRequest(string ExternalReference, string Title, int Quantity, decimal UnitPrice);

    public interface IMercadoPagoGateway
    {
        Task<MercadoPagoPreference> CreatePreferenceAsync(MercadoPagoPreferenceRequest request, CancellationToken cancellationToken);
        Task<MercadoPagoPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken);
        Task<MercadoPagoRefund> RefundFullAsync(string paymentId, CancellationToken cancellationToken);
    }

    public static class MercadoPagoPaymentStatusMapper
    {
        public static PaymentClassification Map(string? rawStatus) => rawStatus?.Trim().ToLowerInvariant() switch
        {
            "approved" => PaymentClassification.Approved,
            "pending" or "in_process" => PaymentClassification.AwaitingResolution,
            "rejected" or "cancelled" => PaymentClassification.NotApprovedTerminal,
            "refunded" or "charged_back" => PaymentClassification.ReversedOrDisputed,
            _ => PaymentClassification.Unknown,
        };
    }

    public sealed class MercadoPagoSignatureVerifier
    {
        private readonly string _webhookSecret;
        private readonly int _timestampToleranceSeconds;
        public MercadoPagoSignatureVerifier(IOptions<MercadoPagoOptions> options)
        {
            _webhookSecret = options.Value.WebhookSecret;
            _timestampToleranceSeconds = options.Value.WebhookTimestampToleranceSeconds;
        }

        public bool IsValid(string? paymentId, string? signatureHeader, string? requestId)
        {
            if (string.IsNullOrWhiteSpace(_webhookSecret) || string.IsNullOrWhiteSpace(paymentId) ||
                string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(requestId)) return false;

            var parts = signatureHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string? timestamp = null;
            string? signature = null;
            foreach (var part in parts)
            {
                var equalsIndex = part.IndexOf('=');
                if (equalsIndex <= 0) continue;
                var key = part[..equalsIndex].Trim();
                var value = part[(equalsIndex + 1)..].Trim();
                if (key.Equals("ts", StringComparison.OrdinalIgnoreCase)) timestamp = value;
                if (key.Equals("v1", StringComparison.OrdinalIgnoreCase)) signature = value;
            }
            if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature)) return false;
            if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestampSeconds)) return false;
            try
            {
                var signedAt = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
                var tolerance = TimeSpan.FromSeconds(Math.Max(0, _timestampToleranceSeconds));
                if (Math.Abs((DateTimeOffset.UtcNow - signedAt).TotalSeconds) > tolerance.TotalSeconds) return false;
                var manifest = $"id:{paymentId};request-id:{requestId};ts:{timestamp};";
                var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_webhookSecret), Encoding.UTF8.GetBytes(manifest));
                var supplied = Convert.FromHexString(signature);
                return CryptographicOperations.FixedTimeEquals(expected, supplied);
            }
            catch (ArgumentOutOfRangeException) { return false; }
            catch (FormatException) { return false; }
        }
    }

    public sealed class MercadoPagoGateway : IMercadoPagoGateway
    {
        private readonly HttpClient _client;
        private readonly MercadoPagoOptions _options;
        public MercadoPagoGateway(HttpClient client, IOptions<MercadoPagoOptions> options)
        {
            _client = client;
            _options = options.Value;
            if (!string.IsNullOrWhiteSpace(_options.AccessToken))
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        public async Task<MercadoPagoPreference> CreatePreferenceAsync(MercadoPagoPreferenceRequest request, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                external_reference = request.ExternalReference,
                items = new[] { new { title = request.Title, quantity = request.Quantity, unit_price = request.UnitPrice, currency_id = "ARS" } },
                back_urls = new { success = _options.CheckoutSuccessUrl, failure = _options.CheckoutFailureUrl, pending = _options.CheckoutPendingUrl }
            });
            using var response = await _client.PostAsync("checkout/preferences", new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            return new MercadoPagoPreference(root.GetProperty("id").GetString() ?? string.Empty,
                root.TryGetProperty("init_point", out var url) ? url.GetString() ?? string.Empty : string.Empty);
        }

        public static string RefundIdempotencyKey(string paymentId)
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"hoydonde:full-refund:{paymentId}"));
            return Convert.ToHexString(digest).ToLowerInvariant();
        }

        public async Task<MercadoPagoPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
        {
            using var response = await _client.GetAsync($"v1/payments/{Uri.EscapeDataString(paymentId)}", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            return new MercadoPagoPayment(
                root.GetProperty("id").ToString(),
                root.GetProperty("status").GetString() ?? string.Empty,
                root.TryGetProperty("external_reference", out var reference) ? reference.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("transaction_amount", out var amount) ? amount.GetDecimal() : 0m,
                root.TryGetProperty("currency_id", out var currency) ? currency.GetString() ?? string.Empty : string.Empty,
                ReadDate(root, "date_approved"));
        }

        public async Task<MercadoPagoRefund> RefundFullAsync(string paymentId, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(paymentId)}/refunds");
            request.Headers.TryAddWithoutValidation("X-Idempotency-Key", RefundIdempotencyKey(paymentId));
            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            return new MercadoPagoRefund(root.GetProperty("id").ToString(), root.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty);
        }

        private static DateTime? ReadDate(JsonElement root, string property) =>
            root.TryGetProperty(property, out var date) && DateTime.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var result)
                ? result.ToUniversalTime() : null;
    }
}
