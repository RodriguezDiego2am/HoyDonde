using HoyDonde.API.Models;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IEventRepository
    {
        Task<Event?> GetByIdAsync(int id);
        Task<IEnumerable<Event>> GetAllAsync();
        Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId);
        Task AddAsync(Event evento);
        Task UpdateAsync(Event evento);
        Task DeleteAsync(Event evento);
    }

}