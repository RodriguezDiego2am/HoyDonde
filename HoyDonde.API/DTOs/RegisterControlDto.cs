namespace HoyDonde.API.DTOs
{
    public class RegisterControlDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int EventId { get; set; } // Evento al que pertenece el Control
        public string OrganizadorId { get; set; } = string.Empty; // Organizador que lo crea
    }
}
