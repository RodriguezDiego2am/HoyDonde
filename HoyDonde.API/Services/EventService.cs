using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using static HoyDonde.API.Models.Event;

public class EventService : IEventService
{
    private readonly IEventRepository _eventRepository;
    private readonly IUserRepository _userRepository; // Para verificar organizador válido

    public EventService(IEventRepository eventRepository, IUserRepository organizerRepository)
    {
        _eventRepository = eventRepository;
        _userRepository = organizerRepository;
    }

    public async Task<Event?> GetByIdAsync(int id)
    {
        return await _eventRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Event>> GetAllAsync()
    {
        return await _eventRepository.GetAllAsync();
    }

    public async Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId)
    {
        return await _eventRepository.GetByOrganizerIdAsync(organizerId);
    }

    public async Task<Event> CreateEventAsync(Event evento)
    {
        // Validación: El organizador debe existir
        var organizer = await _userRepository.GetOrganizerByIdAsync(evento.OrganizadorId);
        if (organizer == null)
            throw new Exception("El organizador no existe.");

        // Validación: Debe haber al menos un grupo de entradas
        if (evento.TicketTypes == null || !evento.TicketTypes.Any())
            throw new Exception("Debe haber al menos un grupo de entradas.");

        // Estado inicial del evento
        evento.Estado = EventStatus.Pendiente;

        await _eventRepository.AddAsync(evento);
        return evento;
    }

    public async Task<bool> UpdateEventAsync(Event evento)
    {
        var existingEvent = await _eventRepository.GetByIdAsync(evento.Id);
        if (existingEvent == null)
            throw new Exception("Evento no encontrado.");

        // Solo se puede modificar si está pendiente o si no tiene ventas
        if (existingEvent.Estado != EventStatus.Pendiente && existingEvent.Asistentes.Any())
            throw new Exception("No se puede modificar un evento con entradas vendidas.");

        existingEvent.Nombre = evento.Nombre;
        existingEvent.Ubicacion = evento.Ubicacion;
        existingEvent.Fecha = evento.Fecha;
        existingEvent.CapacidadMaxima = evento.CapacidadMaxima;

        await _eventRepository.UpdateAsync(existingEvent);
        return true;
    }

    public async Task<bool> PublishEventAsync(int eventId)
    {
        var evento = await _eventRepository.GetByIdAsync(eventId);
        if (evento == null)
            throw new Exception("Evento no encontrado.");

        // Solo un admin puede aprobar el evento (esto se verifica en el controlador)
        evento.Estado = EventStatus.Publicado;

        await _eventRepository.UpdateAsync(evento);
        return true;
    }

    public async Task<bool> CancelEventAsync(int eventId)
    {
        var evento = await _eventRepository.GetByIdAsync(eventId);
        if (evento == null)
            throw new Exception("Evento no encontrado.");

        // Se marca como cancelado y se generan vales
        evento.Estado = EventStatus.Cancelado;

        await _eventRepository.UpdateAsync(evento);
        return true;
    }
}
