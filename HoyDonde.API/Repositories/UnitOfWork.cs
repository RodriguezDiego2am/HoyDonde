using HoyDonde.API.Data;
using HoyDonde.API.Models;
using Microsoft.AspNetCore.Identity;

namespace HoyDonde.API.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;
        private IEventRepository? _eventRepository;
        private IUserRepository? _userRepository;
        private IOrderRepository? _orderRepository;
        private IReservationRepository? _reservationRepository;
        private IPaymentRepository? _paymentRepository;
        private readonly UserManager<ApplicationUser> _userManager;

        public UnitOfWork(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public IEventRepository Events
            => _eventRepository ??= new EventRepository(_context);

        public IUserRepository Users
            => _userRepository ??= new UserRepository(_userManager, _context);

        public IOrderRepository Orders
            => _orderRepository ??= new OrderRepository(_context);

        public IReservationRepository Reservations
            => _reservationRepository ??= new ReservationRepository(_context);

        public IPaymentRepository Payments
            => _paymentRepository ??= new PaymentRepository(_context);

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
