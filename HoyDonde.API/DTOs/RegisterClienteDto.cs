namespace HoyDonde.API.DTOs
{
    public class RegisterClienteDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string DNI { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
    }
}

