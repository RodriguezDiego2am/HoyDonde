using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HoyDonde.API.Controllers
{
    [ApiController]
    [Route("api/events")]
    public class EventsController : ControllerBase
    {
        private readonly IEventService _eventService;
        private readonly ILogger<EventsController> _logger;

        public EventsController(IEventService eventService, ILogger<EventsController> logger)
        {
            _eventService = eventService;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Policy = Acciones.EventoCrear)]
        public async Task<IActionResult> CreateEvent([FromBody] EventCreateRequest request)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            try
            {
                var result = await _eventService.CreateEventAsync(request, organizerId);
                return Ok(result);
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear evento");
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{eventId}/publish")]
        [Authorize(Policy = Acciones.EventoPublicarPropio)]
        public async Task<IActionResult> PublishEvent(string eventId)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            try
            {
                await _eventService.PublishEventAsync(eventId, organizerId);
                return Ok(new { message = "Evento publicado exitosamente." });
            }
            catch (EventNotFoundException)
            {
                return NotFound(new { message = "Evento no encontrado." });
            }
            catch (EventOwnershipException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tenés permiso sobre este evento." });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al publicar evento {EventId}", eventId);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{eventId}/cancel")]
        [Authorize(Policy = Acciones.EventoCancelarPropio)]
        public async Task<IActionResult> CancelEvent(string eventId)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            try
            {
                await _eventService.CancelEventAsync(eventId, organizerId);
                return Ok(new { message = "Evento cancelado exitosamente." });
            }
            catch (EventNotFoundException)
            {
                return NotFound(new { message = "Evento no encontrado." });
            }
            catch (EventOwnershipException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tenés permiso sobre este evento." });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cancelar evento {EventId}", eventId);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{eventId}")]
        [Authorize(Policy = Acciones.EventoEditarPropio)]
        public async Task<IActionResult> UpdateEvent(string eventId, [FromBody] EventUpdateRequest request)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            try
            {
                var result = await _eventService.UpdateEventAsync(eventId, organizerId, request);
                return Ok(result);
            }
            catch (EventNotFoundException)
            {
                return NotFound(new { message = "Evento no encontrado." });
            }
            catch (EventOwnershipException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tenés permiso sobre este evento." });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar evento {EventId}", eventId);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{eventId}")]
        [AllowAnonymous] // Anyone can see public events
        public async Task<IActionResult> GetEvent(string eventId)
        {
            var evento = await _eventService.GetByIdAsync(eventId);
            if (evento == null) return NotFound(new { message = "Evento no encontrado." });
            return Ok(evento);
        }

        [HttpGet]
        [AllowAnonymous] // Anyone can search public events
        public async Task<IActionResult> SearchEvents([FromQuery] EventSearchFilterDto filter)
        {
            var response = await _eventService.SearchEventsAsync(filter);
            return Ok(response);
        }

        [HttpGet("organizer/me")]
        [Authorize(Policy = Acciones.EventoVerPropios)] // Solo el organizador puede ver su "Panel"
        public async Task<IActionResult> GetMyEvents()
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            var events = await _eventService.GetByOrganizerIdAsync(organizerId);
            return Ok(events);
        }

        // UID tomado exclusivamente del token autenticado (nunca de un campo del body/query).
        private string? GetAuthenticatedUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
    }
}
