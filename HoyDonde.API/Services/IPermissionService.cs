using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Resuelve permisos directamente contra la fuente de verdad en Firestore, sin caché
    // (docs/security-refactor-plan.md §4). La optimización con caché queda condicionada a
    // mediciones reales en una etapa posterior.
    public interface IPermissionService
    {
        Task<bool> TieneAccionAsync(string identityProvider, string externalSubjectId, string accionCodigo);

        Task<PermisosEfectivosResult> GetPermisosEfectivosAsync(string identityProvider, string externalSubjectId);

        // Usado por la administración de seguridad (docs/security-refactor-plan.md §6, Etapa 5)
        // para consultar los permisos efectivos de un Usuario ya conocido por UsuarioId, sin
        // pasar por una identidad externa. Misma resolución directa sin caché.
        Task<PermisosEfectivosResult> GetPermisosEfectivosPorUsuarioIdAsync(string usuarioId);
    }

    public record PermisosEfectivosResult(
        string? UsuarioId,
        string? PersonaId,
        bool UsuarioActivo,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Acciones);
}
