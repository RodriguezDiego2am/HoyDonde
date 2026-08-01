using HoyDonde.API.Repositories;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public interface IUserService
    {
        // assignedBy es siempre el actor real que ejecuta el alta (UID de Firebase extraído
        // del token para los endpoints HTTP; el marcador BOOTSTRAP para el comando de
        // aprovisionamiento del primer Administrador). Ver docs/security-refactor-plan.md §2.2/§5.
        Task<UsuarioProvisioningResult> RegisterAdminAsync(string assignedBy, string email, string password);

        Task<UsuarioProvisioningResult> RegisterOrganizadorAsync(string assignedBy, string email, string password);

        // El ownership del evento se valida contra assignedBy (el organizador autenticado)
        // antes de tocar Firebase o Firestore.
        Task<UsuarioProvisioningResult> RegisterControlAsync(string assignedBy, string userName, string password, string eventId);
    }
}
