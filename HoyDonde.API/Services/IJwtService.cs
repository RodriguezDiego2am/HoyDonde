using HoyDonde.API.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace HoyDonde.API.Services
{
    public interface IJwtService
    {
        string GenerateJwtToken(ApplicationUser user, IEnumerable<string> roles);
    }
}

