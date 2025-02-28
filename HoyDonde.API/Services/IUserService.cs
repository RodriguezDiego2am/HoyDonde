using HoyDonde.API.Models;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public interface IUserService
    {
        Task<IdentityResult> RegisterAdminAsync(string email, string password);
        Task<IdentityResult> RegisterOrganizadorAsync(string email, string password);
        Task<IdentityResult> RegisterClienteAsync(string email, string password, string fullName, string dni, string phoneNumber);
        Task<IdentityResult> RegisterControlAsync(string userName, string password, int eventId, string organizadorId);
    }
}

