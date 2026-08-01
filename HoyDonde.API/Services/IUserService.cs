using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public interface IUserService
    {
        Task<bool> RegisterAdminAsync(string email, string password);
        Task<bool> RegisterOrganizadorAsync(string email, string password);
        Task<bool> RegisterClienteAsync(string email, string password, string fullName, string dni, string phoneNumber);
        Task<bool> RegisterControlAsync(string userName, string password, string eventId, string organizadorId);
    }
}
