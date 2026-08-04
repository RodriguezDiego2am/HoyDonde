namespace HoyDonde.API.DTOs
{
    // Respuesta de POST /api/security/usuarios/{usuarioId}/password-reset-link
    // (docs/api-mvp-plan.md §13). El enlace nunca se persiste ni se audita: se muestra al
    // Administrador una única vez para que lo comparta manualmente con el titular de la cuenta.
    public class PasswordResetLinkResponseDto
    {
        public string ResetLink { get; set; } = string.Empty;
    }
}
