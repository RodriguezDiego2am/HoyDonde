using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace HoyDonde.API.Controllers
{
    [Route("api/password")]
    [ApiController]
    public class PasswordController : ControllerBase
    {
        private readonly PasswordResetService _passwordResetService;

        public PasswordController(PasswordResetService passwordResetService)
        {
            _passwordResetService = passwordResetService;
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ResetPasswordDto dto)
        {
            var token = await _passwordResetService.GenerateResetToken(dto.Email);
            if (token == null) return NotFound("Usuario no encontrado.");

            return Ok(new { Message = "Se ha enviado un enlace para restablecer la contraseña.", Token = token });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var result = await _passwordResetService.ResetPassword(dto);
            if (!result) return BadRequest("No se pudo restablecer la contraseña.");

            return Ok("Contraseña restablecida correctamente.");
        }

        [Authorize(Roles = "ADMIN, ORGANIZADOR")]
        [HttpPost("admin-reset-password")]
        public async Task<IActionResult> AdminResetPassword([FromBody] AdminResetPasswordDto dto)
        {
            var result = await _passwordResetService.AdminResetPassword(dto);
            if (!result) return BadRequest("No se pudo restablecer la contraseña.");

            return Ok("Contraseña restablecida correctamente.");
        }
    }
}
