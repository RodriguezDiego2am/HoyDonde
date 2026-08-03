using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    // Lectura de security_audits para el reporte de auditoría (docs/api-mvp-plan.md §11.3/§11.4).
    // Escritura sigue siendo exclusiva de SecurityAuditWriter (siempre dentro de la transacción de
    // la mutación auditada); esta interfaz es de solo lectura.
    public interface ISecurityAuditRepository
    {
        // Único filtro resuelto en la propia query Firestore (Timestamp, índice automático de
        // campo simple): Operacion/ActorUsuarioId/TargetTipo/TargetId se filtran en memoria en el
        // servicio, sobre este conjunto ya acotado por rango. Orden descendente por Timestamp.
        Task<IReadOnlyList<SecurityAudit>> GetByRangoAsync(DateTime desde, DateTime hasta);
    }
}
