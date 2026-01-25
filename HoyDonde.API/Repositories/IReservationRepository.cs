using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public interface IReservationRepository : IRepository<Reservation>
    {
        Task<Reservation?> GetByIdWithDetailsAsync(int id);
        Task<IEnumerable<Reservation>> GetActiveReservationsAsync();
        Task<IEnumerable<Reservation>> GetExpiredReservationsAsync();
        Task<IEnumerable<Reservation>> GetByClienteIdAsync(string clienteId);
    }
}
