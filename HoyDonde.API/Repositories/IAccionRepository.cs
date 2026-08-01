using HoyDonde.API.Models;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IAccionRepository
    {
        Task<bool> ExistsAsync(string codigo);

        // Falla con AccionYaExisteException si el código ya existe (creación estricta, no upsert).
        Task CreateAsync(Accion accion);

        Task<Accion?> GetByCodigoAsync(string codigo);
    }
}
