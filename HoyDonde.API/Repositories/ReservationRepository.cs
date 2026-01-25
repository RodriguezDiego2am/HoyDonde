using HoyDonde.API.Data;
using HoyDonde.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HoyDonde.API.Repositories
{
    public class ReservationRepository : Repository<Reservation>, IReservationRepository
    {
        public ReservationRepository(ApplicationDbContext context) : base(context) { }

        public async Task<Reservation?> GetByIdWithDetailsAsync(int id)
        {
            return await _dbSet
                .Include(r => r.Cliente)
                .Include(r => r.Items)
                    .ThenInclude(ri => ri.TicketType)
                        .ThenInclude(tt => tt.Evento)
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<IEnumerable<Reservation>> GetActiveReservationsAsync()
        {
            return await _dbSet
                .Include(r => r.Items)
                .Where(r => r.Estado == ReservationStatus.Active)
                .ToListAsync();
        }

        public async Task<IEnumerable<Reservation>> GetExpiredReservationsAsync()
        {
            var now = DateTime.UtcNow;
            return await _dbSet
                .Include(r => r.Items)
                    .ThenInclude(ri => ri.TicketType)
                .Where(r => r.Estado == ReservationStatus.Active && r.FechaExpiracion < now)
                .ToListAsync();
        }

        public async Task<IEnumerable<Reservation>> GetByClienteIdAsync(string clienteId)
        {
            return await _dbSet
                .Include(r => r.Items)
                    .ThenInclude(ri => ri.TicketType)
                .Where(r => r.ClienteId == clienteId)
                .OrderByDescending(r => r.FechaCreacion)
                .ToListAsync();
        }
    }
}
