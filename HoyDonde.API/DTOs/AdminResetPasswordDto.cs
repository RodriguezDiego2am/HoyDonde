namespace HoyDonde.API.DTOs
{
    public class AdminResetPasswordDto
    {
        public Guid UserId { get; set; } // Para organizadores y controles
        public string NewPassword { get; set; }
    }
}
