namespace HoyDonde.API.DTOs
{
    // Body de POST /api/auth/sync: solamente datos de Persona. uid/email nunca viajan acá,
    // salen exclusivamente del token (ver AuthController). Sin campo de password: esta API
    // nunca recibe ni almacena una contraseña de Cliente.
    public class SyncClienteRequestDto
    {
        public string? FullName { get; set; }
        public string? Dni { get; set; }
        public string? PhoneNumber { get; set; }
    }
}
