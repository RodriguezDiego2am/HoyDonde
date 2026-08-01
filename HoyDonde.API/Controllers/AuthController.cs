using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HoyDonde.API.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("sync")]
        [Authorize]
        public async Task<IActionResult> SyncUser()
        {
            // Firebase token claims
            var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst("user_id")?.Value 
                      ?? User.FindFirst("sub")?.Value;
            
            var email = User.FindFirst(ClaimTypes.Email)?.Value 
                        ?? User.FindFirst("email")?.Value;

            if (string.IsNullOrEmpty(uid))
            {
                return BadRequest(new { message = "User Identity (UID) missing from token." });
            }

            // Fallback for email/name if not present
            email ??= $"{uid}@placeholder.com"; 
            var name = User.Identity?.Name ?? email;

            var user = await _authService.SyncUserAsync(uid, email, name);
            return Ok(user);
        }
    }
}
