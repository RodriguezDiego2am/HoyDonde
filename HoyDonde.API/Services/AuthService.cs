using HoyDonde.API.Models;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public class AuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IJwtService _jwtService;

        public AuthService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IJwtService jwtService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtService = jwtService;
        }

        public async Task<(bool Succeeded, string? Token, string? Error, IList<string>? Roles)> LoginAsync(
            string identifier, string password)
        {
            var user = await _userManager.FindByEmailAsync(identifier)
                        ?? await _userManager.FindByNameAsync(identifier);

            if (user == null)
                return (false, null, "Usuario/Email no encontrado", null);

            var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
            if (!result.Succeeded)
                return (false, null, "Contraseña inválida", null);

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtService.GenerateJwtToken(user, roles);

            return (true, token, null, roles);
        }
    }
}
