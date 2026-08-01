using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public interface IAuthService
    {
        // uid y email ya vienen resueltos del token de Firebase (nunca del body) por el
        // controller. Ver docs/security-refactor-plan.md §2.1.
        Task<SyncClienteResult> SyncClienteAsync(string uid, string email, SyncClienteRequest request);
    }

    public record SyncClienteRequest(string? FullName, string? Dni, string? PhoneNumber);

    public record SyncClienteResult(
        string UsuarioId,
        string PersonaId,
        IReadOnlyList<string> Roles,
        bool ClaimsUpdated);
}
