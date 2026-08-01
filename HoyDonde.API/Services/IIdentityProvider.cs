using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Abstrae el proveedor de identidad externo (hoy Firebase Auth) para que los servicios de
    // dominio (UserService, etc.) no dependan directamente de FirebaseAuth.DefaultInstance.
    // Etapa 0 del refactor del módulo de seguridad: esta interfaz define el contrato que las
    // etapas siguientes usarán para la creación/compensación de identidades y la migración.
    public interface IIdentityProvider
    {
        Task<IdentityCreationResult> CreateIdentityAsync(string email, string password, string? displayName = null);

        Task DeleteIdentityAsync(string externalSubjectId);

        Task SetActiveAsync(string externalSubjectId, bool isActive);

        Task UpdateAttributesAsync(string externalSubjectId, IdentityAttributeUpdate update);

        Task<string> GeneratePasswordResetLinkAsync(string externalSubjectId);
    }

    public record IdentityCreationResult(string ExternalSubjectId, string IdentityProvider);

    public record IdentityAttributeUpdate(string? Email = null, string? DisplayName = null, string? PhoneNumber = null);
}
