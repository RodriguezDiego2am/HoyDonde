using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;

        public AuthService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<ApplicationUser> SyncUserAsync(string uid, string email, string userName)
        {
            var existingUser = await _userRepository.GetUserByIdAsync(uid);
            if (existingUser != null)
            {
                return existingUser;
            }

            // Create new user (defaulting to Organizador for now as most logic assumes it)
            var newUser = new Organizador
            {
                Id = uid,
                Email = email,
                UserName = userName,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _userRepository.CreateUserAsync(newUser);
            return newUser;
        }
    }
}
