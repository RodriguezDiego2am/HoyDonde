using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace HoyDonde.API.Controllers
{
    [Route("api/users")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost("admin")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> RegisterAdmin([FromBody] RegisterAdminDto request)
        {
            try
            {
                await _userService.RegisterAdminAsync(request.Email, request.Password);
                return Ok(new { message = "Administrador creado exitosamente." });
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
            try
            {
                await _userService.RegisterOrganizadorAsync(request.Email, request.Password);
                return Ok(new { message = "Organizador creado exitosamente." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("cliente")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterCliente([FromBody] RegisterClienteDto request)
        {
            try
            {
                await _userService.RegisterClienteAsync(request.Email, request.Password, request.FullName, request.DNI, request.PhoneNumber);
                return Ok(new { message = "Cliente creado exitosamente." });
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
            try
            {
                var organizadorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                        ?? User.FindFirst("user_id")?.Value
                        ?? User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(organizadorId)) return Unauthorized();

                await _userService.RegisterControlAsync(request.UserName, request.Password, request.EventId, organizadorId);
                return Ok(new { message = "Control creado exitosamente." });
            }
            catch (EventNotFoundException)
            {
                return NotFound(new { message = "Evento no encontrado." });
            }
            catch (EventOwnershipException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tenés permiso sobre este evento." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
