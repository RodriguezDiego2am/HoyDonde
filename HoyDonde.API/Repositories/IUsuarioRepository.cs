using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IUsuarioRepository
    {
        // Crea, en una única transacción Firestore, Persona + Usuario + UsuarioRol inicial +
        // IdentidadExterna. PersonaId/UsuarioId ya vienen generados en el request (el llamador
        // los genera antes de invocar esto, no se generan dentro de la transacción). Idempotente:
        // si ya existe una IdentidadExterna para ese (IdentityProvider, ExternalSubjectId), no
        // vuelve a escribir nada y devuelve los IDs ya existentes en vez de los del request
        // (docs/security-refactor-plan.md §6).
        Task<UsuarioProvisioningResult> ProvisionarAsync(UsuarioProvisioningRequest request);

        Task<string?> GetUsuarioIdByExternalSubjectAsync(string identityProvider, string externalSubjectId);

        Task<Usuario?> GetByIdAsync(string usuarioId);

        Task<IReadOnlyList<string>> GetRolCodigosActivosAsync(string usuarioId);
    }

    public record UsuarioProvisioningRequest(
        string PersonaId,
        string UsuarioId,
        string IdentityProvider,
        string ExternalSubjectId,
        string Email,
        string RolCodigo,
        string AssignedBy,
        string? FullName = null,
        string? Dni = null,
        string? PhoneNumber = null);

    public record UsuarioProvisioningResult(string PersonaId, string UsuarioId);
}
