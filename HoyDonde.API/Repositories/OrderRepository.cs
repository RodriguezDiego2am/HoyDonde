using HoyDonde.API.Data;
using HoyDonde.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HoyDonde.API.Repositories
{
    public class OrderRepository : Repository<Order>, IOrderRepository
    {
        public OrderRepository(ApplicationDbContext context) : base(context) { }

        public async Task<Order?> GetByIdWithDetailsAsync(int id)
        {
            return await _dbSet
                .Include(o => o.Cliente)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.TicketType)
                        .ThenInclude(tt => tt.Evento)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Tickets)
                .Include(o => o.Payments)
                .Include(o => o.Reservation)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<Order?> GetByOrderNumberAsync(string orderNumber)
        {
            return await _dbSet
                .Include(o => o.Cliente)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.TicketType)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
        }

        public async Task<IEnumerable<Order>> GetByClienteIdAsync(string clienteId)
        {
            return await _dbSet
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.TicketType)
                        .ThenInclude(tt => tt.Evento)
                .Include(o => o.Payments)
                .Where(o => o.ClienteId == clienteId)
                .OrderByDescending(o => o.FechaCreacion)
                .ToListAsync();
        }

        public async Task<IEnumerable<Order>> GetPendingOrdersAsync()
        {
            return await _dbSet
                .Where(o => o.Estado == OrderStatus.Pending || o.Estado == OrderStatus.Processing)
                .ToListAsync();
        }

        public async Task<IEnumerable<Order>> GetExpiredOrdersAsync(DateTime expirationTime)
        {
            return await _dbSet
                .Include(o => o.Reservation)
                .Where(o => (o.Estado == OrderStatus.Pending || o.Estado == OrderStatus.Processing)
                    && o.FechaCreacion < expirationTime)
                .ToListAsync();
        }
    }
}
