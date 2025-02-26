using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IEventService
    {
        Task<Event?> GetByIdAsync(int id);
        Task<IEnumerable<Event>> GetAllAsync();
        Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId);
        Task<Event> CreateEventAsync(Event evento);
        Task<bool> UpdateEventAsync(Event evento);
        Task<bool> PublishEventAsync(int eventId);
        Task<bool> CancelEventAsync(int eventId);
    }
}
