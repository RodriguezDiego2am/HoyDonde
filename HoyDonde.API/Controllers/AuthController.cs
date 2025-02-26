using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto loginDto)
        {
            var (Succeeded, Token, Error, Roles) = await _authService.LoginAsync(loginDto.Identifier, loginDto.Password);

            if (!Succeeded || Token == null)
                return Unauthorized(new { message = Error });

            return Ok(new AuthResponseDto
            {
                Token = Token,
                UserName = loginDto.Identifier,
                Roles = Roles.ToList()
            });
        }
    }
}
