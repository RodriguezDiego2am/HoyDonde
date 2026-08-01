using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HoyDonde.API.Controllers
{
    // Administración de roles/acciones asignadas/usuarios (docs/security-refactor-plan.md §6,
    // Etapa 5). Las lecturas administrativas (listar/obtener Rol) se protegen con la policy
    // ROL_EDITAR: es la acción de catálogo más cercana al recurso "Rol" (hoy solo la tiene
    // ADMINISTRADOR), y leer es un subconjunto razonable de poder editar -evita sumar una
    // 21ª acción solo para lecturas-.
    [Route("api/security")]
    [ApiController]
    public class SecurityAdminController : ControllerBase
    {
        private readonly ISecurityAdminService _service;

        public SecurityAdminController(ISecurityAdminService service)
        {
            _service = service;
        }

        private string? GetActorExternalSubjectId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;

        [HttpPost("roles")]
        [Authorize(Policy = Acciones.RolCrear)]
        public async Task<IActionResult> CrearRol([FromBody] CreateRolRequestDto request)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                var result = await _service.CrearRolAsync(actor, request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (RolYaExisteException)
            {
                return Conflict(new { message = "Ya existe un rol con ese código." });
            }
        }

        [HttpPut("roles/{codigo}")]
        [Authorize(Policy = Acciones.RolEditar)]
        public async Task<IActionResult> EditarRol(string codigo, [FromBody] UpdateRolRequestDto request)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                var result = await _service.EditarRolAsync(actor, codigo, request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (RolNoEncontradoException)
            {
                return NotFound(new { message = "Rol no encontrado." });
            }
        }

        [HttpPost("roles/{codigo}/activar")]
        [Authorize(Policy = Acciones.RolActivar)]
        public Task<IActionResult> ActivarRol(string codigo) => SetRolActivoAsync(codigo, true);

        [HttpPost("roles/{codigo}/desactivar")]
        [Authorize(Policy = Acciones.RolActivar)]
        public Task<IActionResult> DesactivarRol(string codigo) => SetRolActivoAsync(codigo, false);

        private async Task<IActionResult> SetRolActivoAsync(string codigo, bool activo)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                await _service.SetRolActivoAsync(actor, codigo, activo);
                return Ok(new { message = activo ? "Rol activado." : "Rol desactivado." });
            }
            catch (RolNoEncontradoException)
            {
                return NotFound(new { message = "Rol no encontrado." });
            }
            catch (UltimoAdministradorException)
            {
                return Conflict(new { message = "La operación dejaría el sistema sin ningún Administrador efectivo." });
            }
        }

        [HttpGet("roles")]
        [Authorize(Policy = Acciones.RolEditar)]
        public async Task<IActionResult> ListarRoles()
        {
            var roles = await _service.ListarRolesAsync();
            return Ok(roles);
        }

        [HttpGet("roles/{codigo}")]
        [Authorize(Policy = Acciones.RolEditar)]
        public async Task<IActionResult> ObtenerRol(string codigo)
        {
            try
            {
                var rol = await _service.ObtenerRolAsync(codigo);
                return Ok(rol);
            }
            catch (RolNoEncontradoException)
            {
                return NotFound(new { message = "Rol no encontrado." });
            }
        }

        [HttpGet("acciones")]
        [Authorize(Policy = Acciones.RolAsignarAccion)]
        public async Task<IActionResult> ListarAcciones()
        {
            var acciones = await _service.ListarAccionesAsync();
            return Ok(acciones);
        }

        [HttpPost("roles/{rolCodigo}/acciones/{accionCodigo}")]
        [Authorize(Policy = Acciones.RolAsignarAccion)]
        public async Task<IActionResult> AsignarAccion(string rolCodigo, string accionCodigo)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                await _service.AsignarAccionARolAsync(actor, rolCodigo, accionCodigo);
                return Ok(new { message = "Acción asignada al rol." });
            }
            catch (RolNoEncontradoException)
            {
                return NotFound(new { message = "Rol no encontrado." });
            }
            catch (AccionNoEncontradaException)
            {
                return NotFound(new { message = "Acción no encontrada." });
            }
        }

        [HttpDelete("roles/{rolCodigo}/acciones/{accionCodigo}")]
        [Authorize(Policy = Acciones.RolQuitarAccion)]
        public async Task<IActionResult> QuitarAccion(string rolCodigo, string accionCodigo)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                await _service.QuitarAccionDeRolAsync(actor, rolCodigo, accionCodigo);
                return Ok(new { message = "Acción quitada del rol." });
            }
            catch (RolNoEncontradoException)
            {
                return NotFound(new { message = "Rol no encontrado." });
            }
        }

        [HttpPost("usuarios/{usuarioId}/roles/{rolCodigo}")]
        [Authorize(Policy = Acciones.UsuarioAsignarRol)]
        public async Task<IActionResult> AsignarRolUsuario(string usuarioId, string rolCodigo)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                await _service.AsignarRolAUsuarioAsync(actor, usuarioId, rolCodigo);
                return Ok(new { message = "Rol asignado al usuario." });
            }
            catch (RolNoEncontradoException)
            {
                return NotFound(new { message = "Rol no encontrado." });
            }
            catch (UsuarioNoEncontradoException)
            {
                return NotFound(new { message = "Usuario no encontrado." });
            }
        }

        [HttpDelete("usuarios/{usuarioId}/roles/{rolCodigo}")]
        [Authorize(Policy = Acciones.UsuarioQuitarRol)]
        public async Task<IActionResult> QuitarRolUsuario(string usuarioId, string rolCodigo)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                await _service.QuitarRolDeUsuarioAsync(actor, usuarioId, rolCodigo);
                return Ok(new { message = "Rol quitado del usuario." });
            }
            catch (RolNoEncontradoException)
            {
                return NotFound(new { message = "Rol no encontrado." });
            }
            catch (UsuarioNoEncontradoException)
            {
                return NotFound(new { message = "Usuario no encontrado." });
            }
            catch (UltimoAdministradorException)
            {
                return Conflict(new { message = "La operación dejaría el sistema sin ningún Administrador efectivo." });
            }
        }

        [HttpGet("usuarios")]
        [Authorize(Policy = Acciones.UsuarioVerPermisosEfectivos)]
        public async Task<IActionResult> ListarUsuarios()
        {
            var usuarios = await _service.ListarUsuariosAsync();
            return Ok(usuarios);
        }

        [HttpGet("usuarios/{usuarioId}/permisos-efectivos")]
        [Authorize(Policy = Acciones.UsuarioVerPermisosEfectivos)]
        public async Task<IActionResult> PermisosEfectivos(string usuarioId)
        {
            try
            {
                var permisos = await _service.ConsultarPermisosEfectivosAsync(usuarioId);
                return Ok(permisos);
            }
            catch (UsuarioNoEncontradoException)
            {
                return NotFound(new { message = "Usuario no encontrado." });
            }
        }

        [HttpPost("usuarios/{usuarioId}/activar")]
        [Authorize(Policy = Acciones.UsuarioDesactivar)]
        public Task<IActionResult> ActivarUsuario(string usuarioId) => SetUsuarioActivoAsync(usuarioId, true);

        [HttpPost("usuarios/{usuarioId}/desactivar")]
        [Authorize(Policy = Acciones.UsuarioDesactivar)]
        public Task<IActionResult> DesactivarUsuario(string usuarioId) => SetUsuarioActivoAsync(usuarioId, false);

        private async Task<IActionResult> SetUsuarioActivoAsync(string usuarioId, bool activo)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();

            try
            {
                await _service.SetUsuarioActivoAsync(actor, usuarioId, activo);
                return Ok(new { message = activo ? "Usuario activado." : "Usuario desactivado." });
            }
            catch (UsuarioNoEncontradoException)
            {
                return NotFound(new { message = "Usuario no encontrado." });
            }
            catch (UltimoAdministradorException)
            {
                return Conflict(new { message = "La operación dejaría el sistema sin ningún Administrador efectivo." });
            }
        }
    }
}
