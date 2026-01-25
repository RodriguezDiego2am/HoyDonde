namespace HoyDonde.API.Repositories
{
    public interface IUnitOfWork : IDisposable
    {
        IEventRepository Events { get; }
        IUserRepository Users { get; }
        IOrderRepository Orders { get; }
        IReservationRepository Reservations { get; }
        IPaymentRepository Payments { get; }
        Task<int> SaveChangesAsync();
    }
}
