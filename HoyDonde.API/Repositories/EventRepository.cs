using HoyDonde.API.Data;
using HoyDonde.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class EventRepository : Repository<Event>, IEventRepository
    {
        public EventRepository(ApplicationDbContext context) : base(context) { }

        public async Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId)
        {
            return await _dbSet
                .Where(e => e.OrganizadorId == organizerId)
                .Include(e => e.TicketTypes)
                .Include(e => e.Asistentes)
                .ToListAsync();
        }

        public override async Task<IEnumerable<Event>> GetAllAsync()
        {
            return await _dbSet
                .Include(e => e.TicketTypes)
                .Include(e => e.Asistentes)
                .ToListAsync();
        }

        public override async Task<Event?> GetByIdAsync(object id)
        {
            return await _dbSet
                .Include(e => e.TicketTypes)
                .Include(e => e.Asistentes)
                .FirstOrDefaultAsync(e => e.Id.Equals(id));
        }

        // Los métodos AddAsync, Update y Remove se usan tal como están en Repository<T>
        // Y la confirmación de cambios se delega a UnitOfWork.
    }
}
