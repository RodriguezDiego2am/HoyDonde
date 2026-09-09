using System.Security.Claims;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoyDonde.API.Controllers
{
    [ApiController]
    [Route("api/backups")]
    [Authorize(Policy = Acciones.BackupAdministrar)]
    public class BackupsController : ControllerBase
    {
        private readonly IFirestoreBackupService _service;

        public BackupsController(IFirestoreBackupService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            var backups = await _service.ListAsync();
            return Ok(backups);
        }

        [HttpPost]
        public async Task<IActionResult> CrearManual()
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();
            var backup = await _service.CreateManualAsync(actor);
            return Accepted(backup);
        }

        [HttpPost("{backupId}/restore")]
        public async Task<IActionResult> Restaurar(string backupId)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();
            var restore = await _service.RestoreAsync(actor, backupId);
            return Accepted(restore);
        }

        [HttpDelete("{backupId}")]
        public async Task<IActionResult> Eliminar(string backupId, [FromBody] DeleteFirestoreBackupRequestDto request)
        {
            var actor = GetActorExternalSubjectId();
            if (string.IsNullOrEmpty(actor)) return Unauthorized();
            await _service.DeleteAsync(actor, backupId, request.ConfirmarEliminacion == true);
            return Ok(new DeleteFirestoreBackupResponseDto { Message = "Copia eliminada." });
        }

        private string? GetActorExternalSubjectId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
    }
}
