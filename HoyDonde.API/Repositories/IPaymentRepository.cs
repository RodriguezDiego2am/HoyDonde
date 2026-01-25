using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public interface IPaymentRepository : IRepository<Payment>
    {
        Task<Payment?> GetByTransactionIdAsync(string transactionId);
        Task<IEnumerable<Payment>> GetByOrderIdAsync(int orderId);
        Task<Payment?> GetByIdWithDetailsAsync(int id);
    }
}
