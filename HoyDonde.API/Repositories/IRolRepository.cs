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
    }
}
