using HoyDonde.API.DTOs;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public interface IPasswordResetService
    {
        Task<string?> GenerateResetToken(string email);
        Task<bool> ResetPassword(ResetPasswordDto dto);
        Task<bool> AdminResetPassword(AdminResetPasswordDto dto);
    }
}
