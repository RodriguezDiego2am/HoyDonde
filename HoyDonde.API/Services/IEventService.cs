using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IEventService
    {
        // Lectura interna sin filtro de visibilidad ni ownership: para uso de otros servicios
        // (p. ej. UserService.RegisterControlAsync necesita leer un evento propio en cualquier
        // estado). No usar desde un controller público.
        Task<Event?> GetEventEntityByIdAsync(string id);

        // GET /api/events/{id}, público: null salvo que el evento esté Publicado y no Finalizado
        // (docs/api-mvp-plan.md §0.1/§2).
        Task<EventResponse?> GetByIdAsync(string id);

        // GET /api/events/organizer/{id}: detalle de un evento propio en cualquier estado.
        // Lanza EventNotFoundException/EventOwnershipException.
        Task<EventResponse> GetOwnedByIdAsync(string eventId, string actorId);

        Task<IEnumerable<EventResponse>> GetByOrganizerIdAsync(string organizerId);
        Task<EventResponse> CreateEventAsync(EventCreateRequest request, string organizerId);
        Task<EventResponse> UpdateEventAsync(string eventId, string actorId, EventUpdateRequest request);
        Task PublishEventAsync(string eventId, string actorId);
        Task CancelEventAsync(string eventId, string actorId);
        Task<PagedResponse<EventResponse>> SearchEventsAsync(EventSearchFilterDto filter);
    }
}
