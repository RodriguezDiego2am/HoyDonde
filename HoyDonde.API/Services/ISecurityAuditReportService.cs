using System.Threading.Tasks;
using HoyDonde.API.DTOs;

namespace HoyDonde.API.Services
{
    public interface ISecurityAuditReportService
    {
        // GET /api/reports/admin/security-audits (docs/api-mvp-plan.md §11.3): primer corte
        // aprobado de la auditoría de seguridad para el Administrador.
        Task<SecurityAuditReporteResponseDto> GetSecurityAuditsReportAsync(SecurityAuditReportFilterDto filter);
    }
}
