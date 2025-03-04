using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using static HoyDonde.API.Models.Event;

public class EventService : IEventService
{
    private readonly IUnitOfWork _unitOfWork;

    public EventService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Event?> GetByIdAsync(int id)
    {
        return await _unitOfWork.Events.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Event>> GetAllAsync()
    {
        return await _unitOfWork.Events.GetAllAsync();
    }

    public async Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId)
    {
        return await _unitOfWork.Events.GetByOrganizerIdAsync(organizerId);
    }

    public async Task<Event> CreateEventAsync(Event evento)
    {
        // Validación: El organizador debe existir
        var organizer = await _unitOfWork.Users.GetOrganizerByIdAsync(evento.OrganizadorId);
        if (organizer == null)
            throw new Exception("El organizador no existe.");

        // Validación: Debe haber al menos un grupo de entradas
        if (evento.TicketTypes == null || !evento.TicketTypes.Any())
            throw new Exception("Debe haber al menos un grupo de entradas.");

        // Estado inicial del evento
        evento.Estado = EventStatus.Pendiente;

        await _unitOfWork.Events.AddAsync(evento);
        return evento;
    }

    public async Task<bool> UpdateEventAsync(Event evento)
    {
        var existingEvent = await _unitOfWork.Events.GetByIdAsync(evento.Id);
        if (existingEvent == null)
            throw new Exception("Evento no encontrado.");

        // Solo se puede modificar si está pendiente o si no tiene ventas
        if (existingEvent.Estado != EventStatus.Pendiente && existingEvent.Asistentes.Any())
            throw new Exception("No se puede modificar un evento con entradas vendidas.");

        existingEvent.Nombre = evento.Nombre;
        existingEvent.Ubicacion = evento.Ubicacion;
        existingEvent.Fecha = evento.Fecha;
        existingEvent.CapacidadMaxima = evento.CapacidadMaxima;

        _unitOfWork.Events.Update(evento);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> PublishEventAsync(int eventId)
    {
        var evento = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (evento == null)
            throw new Exception("Evento no encontrado.");

        // Solo un admin puede aprobar el evento (esto se verifica en el controlador)
        evento.Estado = EventStatus.Publicado;

        _unitOfWork.Events.Update(evento);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CancelEventAsync(int eventId)
    {
        var evento = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (evento == null)
            throw new Exception("Evento no encontrado.");

        // Se marca como cancelado y se generan vales
        evento.Estado = EventStatus.Cancelado;

        _unitOfWork.Events.Update(evento);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }
}
