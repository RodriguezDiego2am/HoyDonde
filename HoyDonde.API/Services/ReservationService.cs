using HoyDonde.API.Data;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HoyDonde.API.Services
{
    public class ReservationService : IReservationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IReservationRepository _reservationRepository;

        public ReservationService(ApplicationDbContext context, IReservationRepository reservationRepository)
        {
            _context = context;
            _reservationRepository = reservationRepository;
        }

        public async Task<Reservation> CreateReservationAsync(string clienteId, List<ReservationItemRequest> items, int durationMinutes = 10)
        {
            // Validar que hay stock disponible para todos los items
            foreach (var item in items)
            {
                if (!await HasAvailableStockAsync(item.TicketTypeId, item.Cantidad))
                {
                    var ticketType = await _context.TicketTypes.FindAsync(item.TicketTypeId);
                    throw new InvalidOperationException($"No hay suficiente stock disponible para {ticketType?.Nombre ?? "el tipo de entrada"}");
                }
            }

            // Crear la reserva
            var reservation = new Reservation
            {
                ClienteId = clienteId,
                FechaCreacion = DateTime.UtcNow,
                FechaExpiracion = DateTime.UtcNow.AddMinutes(durationMinutes),
                Estado = ReservationStatus.Active
            };

            // Agregar items
            foreach (var item in items)
            {
                reservation.Items.Add(new ReservationItem
                {
                    TicketTypeId = item.TicketTypeId,
                    Cantidad = item.Cantidad
                });
            }

            await _reservationRepository.AddAsync(reservation);
            await _context.SaveChangesAsync();

            return await _reservationRepository.GetByIdWithDetailsAsync(reservation.Id)
                ?? throw new InvalidOperationException("Error al crear la reserva");
        }

        public async Task<bool> ValidateReservationAsync(int reservationId)
        {
            var reservation = await _reservationRepository.GetByIdAsync(reservationId);
            if (reservation == null)
                return false;

            // Validar que la reserva esté activa y no haya expirado
            return reservation.Estado == ReservationStatus.Active && reservation.FechaExpiracion > DateTime.UtcNow;
        }

        public async Task<Reservation?> GetReservationByIdAsync(int id)
        {
            return await _reservationRepository.GetByIdWithDetailsAsync(id);
        }

        public async Task CompleteReservationAsync(int reservationId, int orderId)
        {
            var reservation = await _reservationRepository.GetByIdWithDetailsAsync(reservationId);
            if (reservation == null)
                throw new ArgumentException($"Reserva con ID {reservationId} no encontrada");

            reservation.Estado = ReservationStatus.Completed;
            reservation.OrderId = orderId;

            _reservationRepository.Update(reservation);
            await _context.SaveChangesAsync();
        }

        public async Task CancelReservationAsync(int reservationId)
        {
            var reservation = await _reservationRepository.GetByIdAsync(reservationId);
            if (reservation == null)
                throw new ArgumentException($"Reserva con ID {reservationId} no encontrada");

            reservation.Estado = ReservationStatus.Cancelled;

            _reservationRepository.Update(reservation);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> HasAvailableStockAsync(int ticketTypeId, int cantidad)
        {
            var ticketType = await _context.TicketTypes.FindAsync(ticketTypeId);
            if (ticketType == null)
                return false;

            // Calcular stock reservado actualmente
            var reservedStock = await _context.ReservationItems
                .Include(ri => ri.Reservation)
                .Where(ri => ri.TicketTypeId == ticketTypeId
                    && ri.Reservation.Estado == ReservationStatus.Active
                    && ri.Reservation.FechaExpiracion > DateTime.UtcNow)
                .SumAsync(ri => ri.Cantidad);

            // Calcular stock vendido (tickets ya generados)
            var soldStock = await _context.Tickets
                .Where(t => t.TicketTypeId == ticketTypeId)
                .CountAsync();

            // Stock disponible = total - vendido - reservado
            var availableStock = ticketType.CantidadDisponible - soldStock - reservedStock;

            return availableStock >= cantidad;
        }

        public async Task ReleaseExpiredReservationsAsync()
        {
            var expiredReservations = await _reservationRepository.GetExpiredReservationsAsync();

            foreach (var reservation in expiredReservations)
            {
                reservation.Estado = ReservationStatus.Expired;
                _reservationRepository.Update(reservation);
            }

            if (expiredReservations.Any())
            {
                await _context.SaveChangesAsync();
            }
        }
    }
}
