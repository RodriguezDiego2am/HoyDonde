using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Repositories;

namespace HoyDonde.API.Services
{
    // Reporte de solo lectura de auditoría de seguridad (docs/api-mvp-plan.md §11.3, primer
    // corte aprobado): rango sobre Timestamp resuelto en Firestore (ISecurityAuditRepository);
    // Operacion/ActorUsuarioId/TargetTipo/TargetId se filtran en memoria sobre ese conjunto ya
    // acotado (volumen esperado bajo, solo mutaciones administrativas). ActorEmail se resuelve en
    // batch, nunca por actor.
    public class SecurityAuditReportService : ISecurityAuditReportService
    {
        // Período por defecto cuando el caller no informa ningún extremo del rango
        // (docs/api-mvp-plan.md §11.1).
        private const int DefaultRangoDias = 30;

        private readonly ISecurityAuditRepository _auditRepository;
        private readonly IUsuarioRepository _usuarioRepository;

        public SecurityAuditReportService(ISecurityAuditRepository auditRepository, IUsuarioRepository usuarioRepository)
        {
            _auditRepository = auditRepository;
            _usuarioRepository = usuarioRepository;
        }

        public async Task<SecurityAuditReporteResponseDto> GetSecurityAuditsReportAsync(SecurityAuditReportFilterDto filter)
        {
            var (desde, hasta) = ReporteFiltroValidator.ValidateRangoConDefault(filter.FechaDesde, filter.FechaHasta, DefaultRangoDias);

            var audits = await _auditRepository.GetByRangoAsync(desde, hasta);

            var targetTipoFiltro = filter.TargetTipo?.ToString();
            var filtrados = audits
                .Where(a => string.IsNullOrEmpty(filter.Operacion) || a.Operacion == filter.Operacion)
                .Where(a => string.IsNullOrEmpty(filter.ActorUsuarioId) || a.ActorUsuarioId == filter.ActorUsuarioId)
                .Where(a => targetTipoFiltro == null || a.TargetTipo == targetTipoFiltro)
                .Where(a => string.IsNullOrEmpty(filter.TargetId) || a.TargetId == filter.TargetId)
                .OrderByDescending(a => a.Timestamp)
                .ToList();

            var actorIds = filtrados.Select(a => a.ActorUsuarioId).Distinct();
            var actores = await _usuarioRepository.GetByIdsAsync(actorIds);
            var emailPorActor = actores.ToDictionary(u => u.Id, u => u.Email);

            return new SecurityAuditReporteResponseDto
            {
                FechaDesde = desde,
                FechaHasta = hasta,
                Auditorias = filtrados.Select(a => new SecurityAuditReporteDto
                {
                    Timestamp = a.Timestamp,
                    Operacion = a.Operacion,
                    ActorUsuarioId = a.ActorUsuarioId,
                    ActorEmail = emailPorActor.TryGetValue(a.ActorUsuarioId, out var email) ? email : null,
                    TargetTipo = a.TargetTipo,
                    TargetId = a.TargetId,
                    Detalle = a.Detalle,
                }).ToList(),
            };
        }
    }
}
