namespace HoyDonde.API.DTOs
{
    public class ResetPasswordDto
    {
        public string Email { get; set; } // Para clientes
        public string NewPassword { get; set; }
        public string Token { get; set; } // Solo clientes usan token
    }
}