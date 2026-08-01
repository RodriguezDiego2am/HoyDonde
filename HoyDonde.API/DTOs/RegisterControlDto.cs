namespace HoyDonde.API.DTOs
{
    public class RegisterControlDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty; // Evento al que pertenece el Control
        // El OrganizadorId puede venir del auth token o de este campo
        public string OrganizadorId { get; set; } = string.Empty;
    }
}
