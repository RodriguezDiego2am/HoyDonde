using System;

namespace HoyDonde.API.DTOs
{
    // Fila del reporte de auditoría de seguridad (docs/api-mvp-plan.md §11.3). ActorEmail se
    // resuelve en batch desde Usuario (nunca ExternalSubjectId/UID de Firebase); null si el
    // Usuario actor ya no existe. ActorUsuarioId sí se expone -mismo criterio que
    // UsuarioResumenResponseDto.UsuarioId en /api/security/usuarios- para poder distinguir actores
    // cuando ActorEmail no se resuelve.
    public class SecurityAuditReporteDto
    {
        public DateTime Timestamp { get; set; }
        public string Operacion { get; set; } = string.Empty;
        public string ActorUsuarioId { get; set; } = string.Empty;
        public string? ActorEmail { get; set; }
        public string TargetTipo { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
    }
}
