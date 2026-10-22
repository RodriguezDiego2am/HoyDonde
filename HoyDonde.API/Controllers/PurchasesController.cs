using System.Security.Claims;
using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HoyDonde.API.Controllers
{
    [ApiController]
    [Route("api/purchases")]
    [Authorize]
    public sealed class PurchasesController : ControllerBase
    {
        private readonly IOptions<MercadoPagoOptions> _options;
        private readonly IPurchaseCheckoutService _checkout;
        private readonly IAuthenticatedPersonaResolver _personaResolver;
        public PurchasesController(IOptions<MercadoPagoOptions> options, IPurchaseCheckoutService checkout, IAuthenticatedPersonaResolver personaResolver)
        {
            _options = options;
            _checkout = checkout;
            _personaResolver = personaResolver;
        }

        [HttpPost]
        [Authorize(Policy = Acciones.TicketComprar)]
        public async Task<IActionResult> Start([FromBody] PurchaseStartRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        {
            // New checkout initiation remains fail-closed until an operator explicitly enables it.
            if (!_options.Value.Enabled) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return BadRequest(new { message = "Se requiere Idempotency-Key." });
            var customerUid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(customerUid)) return Unauthorized();
            var customerPersonaId = await _personaResolver.ResolvePersonaIdAsync(customerUid);
            var response = await _checkout.StartAsync(customerPersonaId, request, idempotencyKey, HttpContext.RequestAborted);
            return StatusCode(StatusCodes.Status201Created, response);
        }

        [HttpGet("{purchaseId}")]
        [Authorize(Policy = Acciones.TicketVerPropio)]
        public async Task<IActionResult> Get(string purchaseId)
        {
            var customerUid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(customerUid)) return Unauthorized();
            var customerPersonaId = await _personaResolver.ResolvePersonaIdAsync(customerUid);
            var response = await _checkout.GetOwnedAsync(customerPersonaId, purchaseId, HttpContext.RequestAborted);
            return response is null ? NotFound() : Ok(response);
        }
    }
}
