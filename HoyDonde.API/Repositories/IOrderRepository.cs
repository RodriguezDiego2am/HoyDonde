using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public interface IOrderRepository : IRepository<Order>
    {
        Task<Order?> GetByIdWithDetailsAsync(int id);
        Task<Order?> GetByOrderNumberAsync(string orderNumber);
        Task<IEnumerable<Order>> GetByClienteIdAsync(string clienteId);
        Task<IEnumerable<Order>> GetPendingOrdersAsync();
        Task<IEnumerable<Order>> GetExpiredOrdersAsync(DateTime expirationTime);
    }
}
