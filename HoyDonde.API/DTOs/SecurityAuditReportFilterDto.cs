using System;

namespace HoyDonde.API.DTOs
{
    // Objetivo de una mutación de seguridad auditada (docs/api-mvp-plan.md §11.3/§11.8): valores
    // reales tal como los escribe SecurityAdminService.NuevoAudit. El plan solo enumera
    // Rol/Usuario/RolAccion, pero SetUsuarioActivoAsync usa "Usuario" mientras que
    // AsignarRolAUsuarioAsync/QuitarRolDeUsuarioAsync usan "UsuarioRol" -un cuarto valor real y ya
    // persistido-; se incluye acá para que el filtro por objetivo pueda cubrir también las
    // auditorías de asignación de rol a usuario, la operación más frecuente de /admin/usuarios.
    public enum SecurityAuditTargetTipo
    {
        Rol,
        Usuario,
        RolAccion,
        UsuarioRol,
    }

    // Filtro de GET /api/reports/admin/security-audits (docs/api-mvp-plan.md §11.3). Todo
    // opcional: sin fechaDesde/fechaHasta, el servicio aplica el default de 30 días
    // (ReporteFiltroValidator.ValidateRangoConDefault). TargetId es match exacto, nunca substring.
    public class SecurityAuditReportFilterDto
    {
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public string? Operacion { get; set; }
        public string? ActorUsuarioId { get; set; }
        public SecurityAuditTargetTipo? TargetTipo { get; set; }
        public string? TargetId { get; set; }
    }
}
