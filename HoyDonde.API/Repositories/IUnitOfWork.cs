namespace HoyDonde.API.Repositories
{
    public interface IUnitOfWork : IDisposable
    {
        IEventRepository Events { get; }
        IUserRepository Users { get; }
        Task<int> SaveChangesAsync();
    }
}
