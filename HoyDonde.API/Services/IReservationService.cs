using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IReservationService
    {
        Task<Reservation> CreateReservationAsync(string clienteId, List<ReservationItemRequest> items, int durationMinutes = 10);
        Task<bool> ValidateReservationAsync(int reservationId);
        Task<Reservation?> GetReservationByIdAsync(int id);
        Task CompleteReservationAsync(int reservationId, int orderId);
        Task CancelReservationAsync(int reservationId);
        Task<bool> HasAvailableStockAsync(int ticketTypeId, int cantidad);
        Task ReleaseExpiredReservationsAsync();
    }

    public class ReservationItemRequest
    {
        public int TicketTypeId { get; set; }
        public int Cantidad { get; set; }
    }
}
