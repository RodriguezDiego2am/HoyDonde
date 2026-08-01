using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HoyDonde.API.Controllers
{
    // Altas privilegiadas (Admin/Organizador/Control) sobre el modelo nuevo (Etapa 3 del
    // refactor de seguridad, docs/security-refactor-plan.md §2.2). El alta de Cliente vive en
    // AuthController (POST /api/auth/sync, §2.1), no acá.
    [Route("api/users")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        // El actor sale exclusivamente del token, nunca del body.
        private string? GetActorExternalSubjectId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("user_id")?.Value
                ?? User.FindFirst("sub")?.Value;
        }

        [HttpPost("admin")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> RegisterAdmin([FromBody] RegisterAdminDto request)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                var result = await _userService.RegisterAdminAsync(actor, request.Email, request.Password);
                return Ok(new UsuarioProvisioningResponseDto
                {
                    Message = "Administrador creado exitosamente.",
                    UsuarioId = result.UsuarioId,
                    PersonaId = result.PersonaId,
                });
            }
            catch (IdentityEmailAlreadyExistsException)
            {
                return Conflict(new { message = "Ya existe una cuenta con ese email." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Los organizadores no pueden autorregistrarse: solo un Admin autenticado puede darlos de alta.
        [HttpPost("organizador")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> RegisterOrganizador([FromBody] RegisterOrganizadorDto request)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                var result = await _userService.RegisterOrganizadorAsync(actor, request.Email, request.Password);
                return Ok(new UsuarioProvisioningResponseDto
                {
                    Message = "Organizador creado exitosamente.",
                    UsuarioId = result.UsuarioId,
                    PersonaId = result.PersonaId,
                });
            }
            catch (IdentityEmailAlreadyExistsException)
            {
                return Conflict(new { message = "Ya existe una cuenta con ese email." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("control")]
        [Authorize(Roles = Roles.Organizador)]
        public async Task<IActionResult> RegisterControl([FromBody] RegisterControlDto request)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                var result = await _userService.RegisterControlAsync(actor, request.UserName, request.Password, request.EventId);
                return Ok(new UsuarioProvisioningResponseDto
                {
                    Message = "Control creado exitosamente.",
                    UsuarioId = result.UsuarioId,
                    PersonaId = result.PersonaId,
                });
            }
            catch (EventNotFoundException)
            {
                return NotFound(new { message = "Evento no encontrado." });
            }
            catch (EventOwnershipException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tenés permiso sobre este evento." });
            }
            catch (IdentityEmailAlreadyExistsException)
            {
                return Conflict(new { message = "Ya existe una cuenta con ese email." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
