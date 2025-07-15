using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using static HoyDonde.API.Models.Event;

namespace HoyDonde.API.Services
{
    public class EventService : IEventService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<EventService> _logger;

        public EventService(IUnitOfWork unitOfWork, ILogger<EventService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
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

        public async Task<EventResponse> CreateEventAsync(EventCreateRequest request, string organizerId)
        {
            _logger.LogInformation("Creando evento {Nombre} para organizador {Organizador}", request.Nombre, organizerId);

            var organizer = await _unitOfWork.Users.GetOrganizerByIdAsync(organizerId);
            if (organizer == null)
            {
                _logger.LogError("Organizador {OrganizadorId} no existe", organizerId);
                throw new Exception("El organizador no existe.");
            }

            if (request.TicketGroups == null || !request.TicketGroups.Any())
                throw new Exception("Debe haber al menos un grupo de entradas.");

            if (request.FechaInicio <= DateTime.UtcNow)
                throw new Exception("La fecha del evento debe ser futura.");

            var evento = new Event
            {
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                Fecha = request.FechaInicio,
                Ubicacion = request.Ubicacion,
                Categoria = request.Categoria,
                OrganizadorId = organizerId,
                Estado = EventStatus.Activo
            };

            evento.TicketTypes = request.TicketGroups.Select(g => new TicketType
            {
                Nombre = g.Nombre,
                Precio = g.Precio,
                CantidadDisponible = g.CantidadDisponible
            }).ToList();

            evento.CapacidadMaxima = evento.TicketTypes.Sum(t => t.CantidadDisponible);

            await _unitOfWork.Events.AddAsync(evento);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Evento {Id} creado correctamente", evento.Id);

            return new EventResponse
            {
                Id = evento.Id,
                Nombre = evento.Nombre,
                Descripcion = evento.Descripcion,
                FechaInicio = evento.Fecha,
                Ubicacion = evento.Ubicacion,
                Categoria = evento.Categoria,
                Estado = evento.Estado,
                TicketGroups = evento.TicketTypes.Select(t => new TicketGroupDto
                {
                    Nombre = t.Nombre,
                    Precio = t.Precio,
                    CantidadDisponible = t.CantidadDisponible
                }).ToList()
            };
        }

        public async Task<bool> UpdateEventAsync(Event evento)
        {
            var existingEvent = await _unitOfWork.Events.GetByIdAsync(evento.Id);
            if (existingEvent == null)
                throw new Exception("Evento no encontrado.");

            if (existingEvent.Estado != EventStatus.Pendiente && existingEvent.Asistentes.Any())
                throw new Exception("No se puede modificar un evento con entradas vendidas.");

            existingEvent.Nombre = evento.Nombre;
            existingEvent.Ubicacion = evento.Ubicacion;
            existingEvent.Fecha = evento.Fecha;
            existingEvent.CapacidadMaxima = evento.CapacidadMaxima;

            _unitOfWork.Events.Update(existingEvent);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> PublishEventAsync(int eventId)
        {
            var evento = await _unitOfWork.Events.GetByIdAsync(eventId);
            if (evento == null)
                throw new Exception("Evento no encontrado.");

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

            evento.Estado = EventStatus.Cancelado;

            _unitOfWork.Events.Update(evento);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
    }
}
