using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IEventRepository : IRepository<Event>
    {
        Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId);
    }
}
