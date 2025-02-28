using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public interface IAuthService
    {
        Task<(bool Succeeded, string? Token, string? Error, IList<string>? Roles)> LoginAsync(string identifier, string password);
    }
}