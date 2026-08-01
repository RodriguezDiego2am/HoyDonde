using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IRolRepository
    {
        Task<bool> ExistsAsync(string codigo);

        // Falla con RolYaExisteException si el código ya existe (creación estricta, no upsert).
        Task CreateAsync(Rol rol);

        Task<Rol?> GetByCodigoAsync(string codigo);

        // Set idempotente: asignar dos veces la misma acción al mismo rol no falla ni duplica.
        Task AssignAccionAsync(string rolCodigo, string accionCodigo, string assignedBy);

        Task<IReadOnlyList<string>> GetAccionCodigosAsync(string rolCodigo);

        // Administración de seguridad (docs/security-refactor-plan.md §6, Etapa 5). Cada método
        // escribe la mutación y su SecurityAudit en una única transacción Firestore.
        Task<IReadOnlyList<Rol>> GetAllAsync();

        // Falla con RolYaExisteException si el código ya existe.
        Task CrearAsync(Rol rol, SecurityAudit auditEntry);

        // Solo nombre/descripción; el código es inmutable. Falla con RolNoEncontradoException
        // si el rol no existe. No-op idempotente si nombre y descripción son exactamente los
        // valores actuales (no toca UpdatedAt ni audita).
        Task EditarAsync(string codigo, string nombre, string descripcion, SecurityAudit auditEntry);

        // Falla con RolNoEncontradoException si el rol no existe. No-op idempotente si ya está
        // en el estado pedido (no evalúa el guard ni audita). Si codigo == "ADMINISTRADOR" y
        // activo == false y no era ya así, aplica el guard transaccional del último Administrador
        // (falla con UltimoAdministradorException sin escribir nada si dejaría cero
        // Administradores efectivos).
        Task SetActivoAsync(string codigo, bool activo, SecurityAudit auditEntry);

        // Idempotente: si la Accion ya estaba asignada, no-op (no toca AssignedAt/AssignedBy ni
        // audita). Falla con RolNoEncontradoException/AccionNoEncontradaException si el rol o la
        // acción (del catálogo) no existen. Nunca crea una Accion nueva.
        Task AsignarAccionAsync(string rolCodigo, string accionCodigo, string assignedBy, SecurityAudit auditEntry);

        // Idempotente: si la Accion no está asignada a este Rol, no-op (no audita). Falla con
        // RolNoEncontradoException si el rol no existe, o AccionNoEncontradaException si la
        // Accion no existe en el catálogo (distinto de "existe pero no asignada").
        Task QuitarAccionAsync(string rolCodigo, string accionCodigo, SecurityAudit auditEntry);
    }
}
