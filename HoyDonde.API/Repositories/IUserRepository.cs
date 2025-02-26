using HoyDonde.API.Models;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IUserRepository
    {
        Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password);
        Task<ApplicationUser?> GetUserByEmailAsync(string email);
        Task<ApplicationUser?> GetUserByIdAsync(string id);
        Task<bool> IsAdminCreatedAsync();
        Task<Organizador?> GetOrganizerByIdAsync(string id);
    }
}

