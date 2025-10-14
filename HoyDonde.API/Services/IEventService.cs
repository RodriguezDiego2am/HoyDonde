using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IEventService
    {
        Task<Event?> GetByIdAsync(int id);
        Task<IEnumerable<Event>> GetAllAsync();
        Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId);
        Task<EventResponse> CreateEventAsync(EventCreateRequest request, string organizerId);
        Task<bool> UpdateEventAsync(Event evento);
        Task<bool> PublishEventAsync(int eventId);
        Task<bool> CancelEventAsync(int eventId);
    }
}
