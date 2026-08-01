using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Único punto de resolución reutilizado por los servicios de dominio (Event/Ticket/
    // ControlAsignacion) para convertir el UID de Firebase extraído del token en la PersonaId
    // del dominio, vía Usuario (docs/security-refactor-plan.md §1/§4). No se duplica esta
    // resolución ad hoc en cada controller/servicio.
    public interface IAuthenticatedPersonaResolver
    {
        // Lanza IdentityNotProvisionedException si el token es válido pero no existe Usuario
        // (identidades_externas/FIREBASE#{externalSubjectId}) para ese UID en el modelo nuevo.
        Task<string> ResolvePersonaIdAsync(string externalSubjectId);
    }
}
