using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HoyDonde.API.Controllers
{
    // Controller intencionalmente delgado: ninguna excepción de dominio se mapea acá a un
    // código HTTP. Todas se dejan propagar hasta ExceptionMiddleware, que es el único punto
    // central de ese mapeo (docs/api-mvp-plan.md §5).
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

            var result = await _eventService.CreateEventAsync(request, organizerId);
            return Ok(result);
        }

        [HttpPost("{eventId}/publish")]
        [Authorize(Policy = Acciones.EventoPublicarPropio)]
        public async Task<IActionResult> PublishEvent(string eventId)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            await _eventService.PublishEventAsync(eventId, organizerId);
            return Ok(new { message = "Evento publicado exitosamente." });
        }

        [HttpPost("{eventId}/cancel")]
        [Authorize(Policy = Acciones.EventoCancelarPropio)]
        public async Task<IActionResult> CancelEvent(string eventId)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            await _eventService.CancelEventAsync(eventId, organizerId);
            return Ok(new { message = "Evento cancelado exitosamente." });
        }

        [HttpPut("{eventId}")]
        [Authorize(Policy = Acciones.EventoEditarPropio)]
        public async Task<IActionResult> UpdateEvent(string eventId, [FromBody] EventUpdateRequest request)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            var result = await _eventService.UpdateEventAsync(eventId, organizerId, request);
            return Ok(result);
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

            var result = await _eventService.GetOwnedByIdAsync(id, organizerId);
            return Ok(result);
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

            var asignacion = await _userService.AsignarControlExistenteAsync(organizerId, eventId, controlPersonaId);
            return Ok(new ControlAsignacionResponseDto
            {
                ControlPersonaId = asignacion.ControlPersonaId,
                EventId = asignacion.EventId,
                AssignedByPersonaId = asignacion.AssignedByPersonaId,
                CreatedAt = asignacion.CreatedAt,
            });
        }

        // API-MVP 5 (docs/api-mvp-plan.md §6): Controles del ámbito del organizador autenticado
        // (los que asignó alguna vez, a cualquiera de sus eventos), sin duplicados. Reutiliza
        // CONTROL_CREAR (sin acción nueva).
        [HttpGet("organizer/controls")]
        [Authorize(Policy = Acciones.ControlCrear)]
        public async Task<IActionResult> GetMyControls()
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            var controles = await _userService.ListarControlesDelOrganizadorAsync(organizerId);
            return Ok(controles);
        }

        // API-MVP 5: Controles asignados a un evento propio del organizador autenticado.
        [HttpGet("{eventId}/controls")]
        [Authorize(Policy = Acciones.ControlCrear)]
        public async Task<IActionResult> GetEventControls(string eventId)
        {
            var organizerId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            var controles = await _userService.ListarControlesDelEventoAsync(organizerId, eventId);
            return Ok(controles);
        }

        // API-MVP 5: eventos asignados al Control autenticado, en cualquier estado (la UI decide
        // qué habilitar). Reutiliza TICKET_VALIDAR (sin acción nueva).
        [HttpGet("control/me")]
        [Authorize(Policy = Acciones.TicketValidar)]
        public async Task<IActionResult> GetMyAssignedEvents()
        {
            var controlId = GetAuthenticatedUserId();
            if (string.IsNullOrEmpty(controlId)) return Unauthorized();

            var eventos = await _userService.ListarEventosAsignadosAsync(controlId);
            return Ok(eventos);
        }

        // UID tomado exclusivamente del token autenticado (nunca de un campo del body/query).
        private string? GetAuthenticatedUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
    }
}
