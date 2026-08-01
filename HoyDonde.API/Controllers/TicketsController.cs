using System;
using System.Security.Claims;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    public class TicketsController : ControllerBase
    {
        private readonly ITicketService _ticketService;
        private readonly ILogger<TicketsController> _logger;

        public TicketsController(ITicketService ticketService, ILogger<TicketsController> logger)
        {
            _ticketService = ticketService;
            _logger = logger;
        }

        [HttpPost("buy")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<IActionResult> BuyTickets([FromBody] TicketBuyRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var clienteId = GetAuthenticatedUserId();

            if (string.IsNullOrEmpty(clienteId))
                return Unauthorized();

            try
            {
                var tickets = await _ticketService.BuyTicketsAsync(clienteId, request);
                return Ok(tickets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al comprar tickets para cliente {ClienteId}", clienteId);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("me")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<IActionResult> GetMyTickets()
        {
            var clienteId = GetAuthenticatedUserId();

            if (string.IsNullOrEmpty(clienteId))
                return Unauthorized();

            var tickets = await _ticketService.GetTicketsByClienteIdAsync(clienteId);
            return Ok(tickets);
        }

        [HttpPost("validate")]
        [Authorize(Roles = Roles.Control)]
        public async Task<IActionResult> ValidateTicket([FromQuery] string ticketId, [FromQuery] string eventId)
        {
            if (string.IsNullOrEmpty(ticketId) || string.IsNullOrEmpty(eventId))
                return BadRequest(new { message = "Se requieren ticketId y eventId." });

            // El UID del Control sale exclusivamente del token; nunca se acepta desde el cliente.
            var controlId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(controlId))
                return Unauthorized();

            var outcome = await _ticketService.ValidateTicketAsync(controlId, ticketId, eventId);

            return outcome switch
            {
                TicketValidationOutcome.Success =>
                    Ok(new { valid = true, message = "Ticket validado para este evento." }),
                TicketValidationOutcome.NotAuthorized =>
                    StatusCode(StatusCodes.Status403Forbidden, new { valid = false, message = "No autorizado para validar tickets de este evento." }),
                TicketValidationOutcome.AlreadyUsed =>
                    Conflict(new { valid = false, message = "El ticket ya fue utilizado." }),
                TicketValidationOutcome.Cancelled =>
                    Conflict(new { valid = false, message = "El ticket fue anulado." }),
                _ =>
                    BadRequest(new { valid = false, message = "Ticket inválido o no corresponde al evento." })
            };
        }

        // UID tomado exclusivamente del token autenticado (nunca de un campo del body/query).
        private string? GetAuthenticatedUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
    }
}
