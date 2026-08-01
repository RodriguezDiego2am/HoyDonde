using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public interface IAuthService
    {
        Task<ApplicationUser> SyncUserAsync(string uid, string email, string userName);
    }
}