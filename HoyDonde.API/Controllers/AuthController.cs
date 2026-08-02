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

        // Flujo Cliente (docs/security-refactor-plan.md §2.1, Etapa 3): requiere un ID token de
        // Firebase válido ([Authorize], sin excepción anónima). uid y email salen
        // exclusivamente del token, nunca del body. El body solo trae datos de Persona
        // (nombre, DNI, teléfono) y nunca una contraseña.
        [HttpPost("sync")]
        [Authorize]
        public async Task<IActionResult> SyncUser([FromBody] SyncClienteRequestDto? request)
        {
            var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("user_id")?.Value
                      ?? User.FindFirst("sub")?.Value;

            var email = User.FindFirst(ClaimTypes.Email)?.Value
                        ?? User.FindFirst("email")?.Value;

            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(email))
            {
                return BadRequest(new { message = "El token no trae UID o email." });
            }

            var syncRequest = new SyncClienteRequest(request?.FullName, request?.Dni, request?.PhoneNumber);
            var result = await _authService.SyncClienteAsync(uid, email, syncRequest);

            return Ok(new SyncUserResponseDto
            {
                UsuarioId = result.UsuarioId,
                PersonaId = result.PersonaId,
                Roles = result.Roles,
                Acciones = result.Acciones,
            });
        }
    }
}
