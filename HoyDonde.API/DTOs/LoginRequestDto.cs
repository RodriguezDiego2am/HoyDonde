namespace HoyDonde.API.DTOs
{
    public class LoginRequestDto
    {
        public string Identifier { get; set; } = null!; // Email o Username
        public string Password { get; set; } = null!;
    }
}
