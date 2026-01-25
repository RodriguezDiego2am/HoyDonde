using HoyDonde.API.Data;
using HoyDonde.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HoyDonde.API.Repositories
{
    public class PaymentRepository : Repository<Payment>, IPaymentRepository
    {
        public PaymentRepository(ApplicationDbContext context) : base(context) { }

        public async Task<Payment?> GetByTransactionIdAsync(string transactionId)
        {
            return await _dbSet
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.TransactionId == transactionId);
        }

        public async Task<IEnumerable<Payment>> GetByOrderIdAsync(int orderId)
        {
            return await _dbSet
                .Where(p => p.OrderId == orderId)
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();
        }

        public async Task<Payment?> GetByIdWithDetailsAsync(int id)
        {
            return await _dbSet
                .Include(p => p.Order)
                    .ThenInclude(o => o.Cliente)
                .FirstOrDefaultAsync(p => p.Id == id);
        }
    }
}
