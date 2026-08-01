using HoyDonde.API.Models;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IIdentidadHuerfanaRepository
    {
        Task RegistrarAsync(IdentidadHuerfana identidadHuerfana);
    }
}
