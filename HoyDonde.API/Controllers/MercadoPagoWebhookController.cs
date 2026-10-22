using System;
using System.Threading.Tasks;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Controllers
{
    public sealed class MercadoPagoWebhookData { public string Id { get; init; } = string.Empty; }
    public sealed class MercadoPagoWebhookRequest { public MercadoPagoWebhookData? Data { get; init; } }

    [ApiController]
    [AllowAnonymous]
    [Route("api/payments/mercado-pago/webhook")]
    public sealed class MercadoPagoWebhookController : ControllerBase
    {
        private readonly MercadoPagoSignatureVerifier _signatureVerifier;
        private readonly IPaymentWebhookInbox _inbox;
        private readonly ILogger<MercadoPagoWebhookController> _logger;

        public MercadoPagoWebhookController(MercadoPagoSignatureVerifier signatureVerifier, IPaymentWebhookInbox inbox, ILogger<MercadoPagoWebhookController> logger)
        {
            _signatureVerifier = signatureVerifier;
            _inbox = inbox;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Receive([FromBody] MercadoPagoWebhookRequest request, [FromHeader(Name = "x-signature")] string? signature, [FromHeader(Name = "x-request-id")] string? requestId)
        {
            var paymentId = request?.Data?.Id;
            if (!_signatureVerifier.IsValid(paymentId, signature, requestId))
            {
                _logger.LogWarning("Rejected Mercado Pago webhook with an invalid signature.");
                return Unauthorized();
            }

            try
            {
                await _inbox.PersistAsync(paymentId!, DateTime.UtcNow);
                return Created(string.Empty, null);
            }
            catch (Exception exception)
            {
                // A 5xx deliberately requests provider retry; payment identifiers and headers stay out of logs.
                _logger.LogError(exception, "Unable to persist verified Mercado Pago webhook inbox entry.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }
    }
}
