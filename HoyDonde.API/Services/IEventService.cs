using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IEventService
    {
        Task<Event?> GetByIdAsync(string id);
        Task<IEnumerable<Event>> GetAllAsync();
        Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId);
        Task<EventResponse> CreateEventAsync(EventCreateRequest request, string organizerId);
        Task<EventResponse> UpdateEventAsync(string eventId, string actorId, EventUpdateRequest request);
        Task PublishEventAsync(string eventId, string actorId);
        Task CancelEventAsync(string eventId, string actorId);
        Task<PagedResponse<Event>> SearchEventsAsync(EventSearchFilterDto filter);
    }
}
