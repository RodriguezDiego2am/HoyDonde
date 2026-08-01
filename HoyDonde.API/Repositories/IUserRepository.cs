using HoyDonde.API.Models;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IUserRepository
    {
        Task CreateUserAsync(ApplicationUser user);
        Task<ApplicationUser?> GetUserByEmailAsync(string email);
        Task<ApplicationUser?> GetUserByIdAsync(string id);
        Task<bool> IsAdminCreatedAsync();
        Task<Organizador?> GetOrganizerByIdAsync(string id);
        Task UpdateUserAsync(ApplicationUser user, string performedByUserId);
        Task AddUserAuditAsync(string userId, string actionType, string description, string performedBy, object snapshot);
    }
}
