using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IAccionRepository
    {
        Task<bool> ExistsAsync(string codigo);

        // Falla con AccionYaExisteException si el código ya existe (creación estricta, no upsert).
        Task CreateAsync(Accion accion);

        Task<Accion?> GetByCodigoAsync(string codigo);

        // Catálogo completo de Acciones (docs/security-refactor-plan.md §6, Etapa 5), usado por
        // GET /api/security/acciones para que la administración pueda descubrir los objetivos
        // disponibles para asignar/quitar de un Rol.
        Task<IReadOnlyList<Accion>> GetAllAsync();
    }
}
