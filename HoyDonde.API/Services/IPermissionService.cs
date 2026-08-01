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
    }

    public record PermisosEfectivosResult(
        string? UsuarioId,
        string? PersonaId,
        bool UsuarioActivo,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Acciones);
}
