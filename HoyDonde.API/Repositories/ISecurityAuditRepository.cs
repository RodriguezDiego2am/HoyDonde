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

        // Escritura standalone, SIN transacción Firestore emparejada (docs/api-mvp-plan.md §13):
        // usada exclusivamente cuando la mutación auditada ocurrió en un sistema externo (Firebase
        // Auth generando un enlace de reseteo de contraseña), nunca en Firestore -no existe una
        // transacción distribuida entre Firebase Auth y Firestore, así que esto NO es atómico con
        // la llamada a Firebase que la precede: si el proceso cae justo entre ambas, el enlace ya
        // fue emitido por Firebase pero esta auditoría puede faltar-. SecurityAuditWriter sigue
        // siendo el único camino para auditorías que sí acompañan una mutación Firestore real.
        Task RegistrarAsync(SecurityAudit entry);
    }
}
