using HoyDonde.API.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Administración configurable de roles/acciones asignadas/usuarios
    // (docs/security-refactor-plan.md §6, Etapa 5). El actor siempre se resuelve desde el token
    // autenticado (externalSubjectId), nunca desde el body.
    public interface ISecurityAdminService
    {
        Task<RolResponseDto> CrearRolAsync(string actorExternalSubjectId, CreateRolRequestDto request);

        Task<RolResponseDto> EditarRolAsync(string actorExternalSubjectId, string codigo, UpdateRolRequestDto request);

        Task SetRolActivoAsync(string actorExternalSubjectId, string codigo, bool activo);

        Task<IReadOnlyList<RolResponseDto>> ListarRolesAsync();

        Task<RolResponseDto> ObtenerRolAsync(string codigo);

        Task<IReadOnlyList<AccionResponseDto>> ListarAccionesAsync();

        Task<IReadOnlyList<UsuarioResumenResponseDto>> ListarUsuariosAsync();

        Task AsignarAccionARolAsync(string actorExternalSubjectId, string rolCodigo, string accionCodigo);

        Task QuitarAccionDeRolAsync(string actorExternalSubjectId, string rolCodigo, string accionCodigo);

        Task AsignarRolAUsuarioAsync(string actorExternalSubjectId, string usuarioId, string rolCodigo);

        Task QuitarRolDeUsuarioAsync(string actorExternalSubjectId, string usuarioId, string rolCodigo);

        Task<PermisosEfectivosResponseDto> ConsultarPermisosEfectivosAsync(string usuarioId);

        Task SetUsuarioActivoAsync(string actorExternalSubjectId, string usuarioId, bool activo);
    }
}
