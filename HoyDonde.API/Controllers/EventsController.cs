using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
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
        [Authorize(Roles = Roles.Organizador)]
        public async Task<IActionResult> CreateEvent([FromBody] EventCreateRequest request)
        {
            var organizerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(organizerId)) return Unauthorized();

            try
            {
                var result = await _eventService.CreateEventAsync(request, organizerId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear evento");
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
