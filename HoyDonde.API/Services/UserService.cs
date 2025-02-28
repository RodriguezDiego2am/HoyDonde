using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.AspNetCore.Identity;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using System;
using System.Drawing.Imaging;
using System.Drawing;
using System.Threading.Tasks;
namespace HoyDonde.API.Services
{
    public class UserService
    {
        private readonly IUserRepository _userRepository;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserService(IUserRepository userRepository, RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
        {
            _userRepository = userRepository;
            _roleManager = roleManager;
            _userManager = userManager;
        }

        public async Task<IdentityResult> RegisterAdminAsync(string email, string password)
        {
            if (await _userRepository.IsAdminCreatedAsync())
                throw new InvalidOperationException("El administrador ya ha sido creado.");

            var admin = new Admin { UserName = email, Email = email };
            var result = await _userRepository.CreateUserAsync(admin, password);

            if (result.Succeeded)
            {
                await _roleManager.CreateAsync(new IdentityRole("Admin"));
                await _userManager.AddToRoleAsync(admin, "Admin");
            }

            return result;
        }

        public async Task<IdentityResult> RegisterOrganizadorAsync(string email, string password)
        {
            var organizador = new Organizador { UserName = email, Email = email };
            var result = await _userRepository.CreateUserAsync(organizador, password);

            if (result.Succeeded)
                await _userManager.AddToRoleAsync(organizador, "Organizador");

            return result;
        }

        public async Task<IdentityResult> RegisterClienteAsync(string email, string password, string fullName, string dni, string phoneNumber)
        {
        

            var cliente = new Cliente { UserName = email, Email = email, FullName = fullName, DNI = dni, PhoneNumber = phoneNumber};
            var result = await _userRepository.CreateUserAsync(cliente, password);

            if (result.Succeeded)
                await _userManager.AddToRoleAsync(cliente, "Cliente");

            return result;
        }

        public async Task<IdentityResult> RegisterControlAsync(string userName, string password, int eventId, string organizadorId)
        {
            var control = new Control
            {
                UserName = userName,
                Email = $"{userName}@control.hoydonde.com", // Puede no requerir email
                EventId = eventId,
                OrganizadorId = organizadorId
            };

            var result = await _userRepository.CreateUserAsync(control, password);

            if (result.Succeeded)
                await _userManager.AddToRoleAsync(control, "Control");

            return result;
        }
    }
}


