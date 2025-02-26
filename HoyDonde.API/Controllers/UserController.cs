using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace HoyDonde.API.Controllers
{
    [Route("api/users")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly UserService _userService;

        public UserController(UserService userService)
        {
            _userService = userService;
        }

        [HttpPost("register-admin")]
        public async Task<IActionResult> RegisterAdmin([FromBody] RegisterAdminDto model)
        {
            var result = await _userService.RegisterAdminAsync(model.Email, model.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok("Administrador creado correctamente.");
        }

        [HttpPost("register-organizador")]
        public async Task<IActionResult> RegisterOrganizador([FromBody] RegisterOrganizadorDto model)
        {
            var result = await _userService.RegisterOrganizadorAsync(model.Email, model.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok("Organizador creado correctamente.");
        }

        [HttpPost("register-cliente")]
        public async Task<IActionResult> RegisterCliente([FromBody] RegisterClienteDto model)
        {
            var result = await _userService.RegisterClienteAsync(model.Email, model.Password, model.FullName, model.DNI, model.PhoneNumber);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok("Cliente registrado correctamente.");
        }

        [Authorize(Roles = "Organizador")]
        [HttpPost("register-control")]
        public async Task<IActionResult> RegisterControl([FromBody] RegisterControlDto model)
        {
            var result = await _userService.RegisterControlAsync(model.UserName, model.Password, model.EventId, model.OrganizadorId);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok("Usuario de control registrado correctamente.");
        }
    }
}
