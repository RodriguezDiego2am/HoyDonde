using System.Security.Claims;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Controllers
{
    // Controller intencionalmente delgado: las excepciones de dominio se dejan propagar hasta
    // ExceptionMiddleware (docs/api-mvp-plan.md §5). El único mapeo que vive acá es el switch de
    // TicketValidationOutcome, que no es una excepción sino el resultado explícito del dominio.
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
        [Authorize(Policy = Acciones.TicketComprar)]
        public async Task<IActionResult> BuyTickets([FromBody] TicketBuyRequest request)
        {
            var clienteId = GetAuthenticatedUserId();

            if (string.IsNullOrEmpty(clienteId))
                return Unauthorized();

            var tickets = await _ticketService.BuyTicketsAsync(clienteId, request);
            return Ok(tickets);
        }

        [HttpGet("me")]
        [Authorize(Policy = Acciones.TicketVerPropio)]
        public async Task<IActionResult> GetMyTickets()
        {
            var clienteId = GetAuthenticatedUserId();

            if (string.IsNullOrEmpty(clienteId))
                return Unauthorized();

            var tickets = await _ticketService.GetTicketsByClienteIdAsync(clienteId);
            return Ok(tickets);
        }

        [HttpPost("validate")]
        [Authorize(Policy = Acciones.TicketValidar)]
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
                TicketValidationOutcome.Anulado =>
                    Conflict(new { valid = false, message = "El ticket fue anulado." }),
                TicketValidationOutcome.EventoCancelado =>
                    Conflict(new { valid = false, message = "El evento fue cancelado." }),
                TicketValidationOutcome.EventoFinalizado =>
                    Conflict(new { valid = false, message = "El evento ya finalizó." }),
                TicketValidationOutcome.NotFound =>
                    NotFound(new { valid = false, message = "Ticket no encontrado." }),
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
