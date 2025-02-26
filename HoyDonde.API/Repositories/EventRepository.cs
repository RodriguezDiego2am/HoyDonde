using HoyDonde.API.Data;
using HoyDonde.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class EventRepository : IEventRepository
    {
        private readonly ApplicationDbContext _context;

        public EventRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Event?> GetByIdAsync(int id)
        {
            return await _context.Eventos
                .Include(e => e.TicketTypes)
                .Include(e => e.Asistentes)
                .FirstOrDefaultAsync(e => e.Id == id);
        }

        public async Task<IEnumerable<Event>> GetAllAsync()
        {
            return await _context.Eventos
                .Include(e => e.TicketTypes)
                .Include(e => e.Asistentes)
                .ToListAsync();
        }

        public async Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId)
        {
            return await _context.Eventos
                .Where(e => e.OrganizadorId == organizerId)
                .Include(e => e.TicketTypes)
                .Include(e => e.Asistentes)
                .ToListAsync();
        }

        public async Task AddAsync(Event evento)
        {
            await _context.Eventos.AddAsync(evento);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Event evento)
        {
            _context.Eventos.Update(evento);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Event evento)
        {
            _context.Eventos.Remove(evento);
            await _context.SaveChangesAsync();
        }
    }
}
