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
        private readonly IUserService _userService;
        private readonly ILogger<EventsController> _logger;

        public EventsController(IEventService eventService, IUserService userService, ILogger<EventsController> logger)
        {
            _eventService = eventService;
            _userService = userService;
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
            catch (EventValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
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
            catch (EventInvalidTransitionException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (EventMissingTicketTypesException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
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
            catch (EventInvalidTransitionException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
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
            catch (EventNotEditableException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (EventValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
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

        [HttpGet("organizer/{id}")]
        [Authorize(Policy = Acciones.EventoVerPropios)] // Detalle de un evento propio en cualquier estado
        public async Task<IActionResult> GetOwnedEvent(string id)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            try
            {
                var result = await _eventService.GetOwnedByIdAsync(id, organizerId);
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
        }

        // API-MVP 3 (docs/api-mvp-plan.md §4): asigna un Control ya existente a otro evento
        // propio del organizador autenticado. Reutiliza CONTROL_CREAR (sin acción nueva); la
        // ruta cuelga de /api/events, no de /api/users, porque la operación es sobre el
        // evento destino. No crea ninguna cuenta nueva.
        [HttpPost("{eventId}/controls/{controlPersonaId}")]
        [Authorize(Policy = Acciones.ControlCrear)]
        public async Task<IActionResult> AssignControl(string eventId, string controlPersonaId)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            try
            {
                var asignacion = await _userService.AsignarControlExistenteAsync(organizerId, eventId, controlPersonaId);
                return Ok(new ControlAsignacionResponseDto
                {
                    ControlPersonaId = asignacion.ControlPersonaId,
                    EventId = asignacion.EventId,
                    AssignedByPersonaId = asignacion.AssignedByPersonaId,
                    CreatedAt = asignacion.CreatedAt,
                });
            }
            catch (EventNotFoundException)
            {
                return NotFound(new { message = "Evento no encontrado." });
            }
            catch (ControlInvalidoException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (EventOwnershipException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tenés permiso sobre este evento." });
            }
            catch (ControlAjenoException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (EventoNoDisponibleParaAsignacionControlException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (IdentityNotProvisionedException)
            {
                // Se deja propagar al middleware: respuesta 403 genérica centralizada, sin
                // reenviar información interna (docs/security-refactor-plan.md §1/§4).
                throw;
            }
        }

        // UID tomado exclusivamente del token autenticado (nunca de un campo del body/query).
        private string? GetAuthenticatedUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
    }
}
