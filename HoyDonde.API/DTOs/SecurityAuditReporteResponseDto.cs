using System;
using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Respuesta de GET /api/reports/admin/security-audits (docs/api-mvp-plan.md §11.3). FechaDesde/
    // FechaHasta son siempre el rango efectivamente aplicado (incluye el default de 30 días
    // cuando el caller no informó ninguno), nunca lo que el caller mandó crudo.
    public class SecurityAuditReporteResponseDto
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public List<SecurityAuditReporteDto> Auditorias { get; set; } = new();
    }
}
