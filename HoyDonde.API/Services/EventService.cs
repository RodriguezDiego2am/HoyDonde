using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static HoyDonde.API.Models.Event;

namespace HoyDonde.API.Services
{
    public class EventService : IEventService
    {
        private readonly FirestoreDb _firestore;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<EventService> _logger;
        private const string CollectionName = "events";

        public EventService(FirestoreDb firestore, IUserRepository userRepository, ILogger<EventService> logger)
        {
            _firestore = firestore;
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task<Event?> GetByIdAsync(string id)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(id).GetSnapshotAsync();
            if (!snapshot.Exists) return null;
            return snapshot.ConvertTo<Event>();
        }

        public async Task<IEnumerable<Event>> GetAllAsync()
        {
            var snapshot = await _firestore.Collection(CollectionName).GetSnapshotAsync();
            return snapshot.Documents.Select(d => d.ConvertTo<Event>()).ToList();
        }

        public async Task<IEnumerable<Event>> GetByOrganizerIdAsync(string organizerId)
        {
            var query = _firestore.Collection(CollectionName).WhereEqualTo("OrganizadorId", organizerId);
            var snapshot = await query.GetSnapshotAsync();
            return snapshot.Documents.Select(d => d.ConvertTo<Event>()).ToList();
        }

        public async Task<EventResponse> CreateEventAsync(EventCreateRequest request, string organizerId)
        {
            _logger.LogInformation("Creating event {Nombre} for organizer {Organizador}", request.Nombre, organizerId);

            var organizer = await _userRepository.GetOrganizerByIdAsync(organizerId); 
            // Note: If organizer logic is strict, check if user has 'organizer' role or just exists.
            
            // Check existence at least
            var user = await _userRepository.GetUserByIdAsync(organizerId);
            if (user == null)
            {
                throw new Exception("Organizer does not exist.");
            }

            if (request.FechaInicio <= DateTime.UtcNow)
                throw new Exception("Event date must be in the future.");

            var newEvent = new Event
            {
                // Id will be assigned after creation or we generate one now
                Id = Guid.NewGuid().ToString(), // Generating explicit ID
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                Fecha = request.FechaInicio,
                Ubicacion = request.Ubicacion,
                Categoria = request.Categoria,
                OrganizadorId = organizerId,
                Estado = EventStatus.Activo,
                CapacidadMaxima = 0 // Will calc
            };

            // Map Tickets
            if (request.TicketGroups != null)
            {
                newEvent.TicketTypes = request.TicketGroups.Select(g => new TicketType
                {
                    Id = Guid.NewGuid().ToString(),
                    Nombre = g.Nombre,
                    Precio = g.Precio,
                    CantidadDisponible = g.CantidadDisponible,
                    EventoId = newEvent.Id
                }).ToList();
                newEvent.CapacidadMaxima = newEvent.TicketTypes.Sum(t => t.CantidadDisponible);
            }

            // Save to Firestore
            var docRef = _firestore.Collection(CollectionName).Document(newEvent.Id);
            await docRef.SetAsync(newEvent);

            return new EventResponse
            {
                Id = newEvent.Id,
                Nombre = newEvent.Nombre,
                Descripcion = newEvent.Descripcion,
                FechaInicio = newEvent.Fecha,
                Ubicacion = newEvent.Ubicacion,
                Categoria = newEvent.Categoria,
                Estado = newEvent.Estado,
                TicketGroups = newEvent.TicketTypes.Select(t => new TicketGroupDto
                {
                    Nombre = t.Nombre,
                    Precio = t.Precio,
                    CantidadDisponible = t.CantidadDisponible
                }).ToList()
            };
        }

        public async Task<EventResponse> UpdateEventAsync(string eventId, string actorId, EventUpdateRequest request)
        {
            var evento = await GetOwnedEventOrThrowAsync(eventId, actorId);

            // No se toca el ciclo de estados (Estado) ni la propiedad (OrganizadorId) desde acá.
            evento.Nombre = request.Nombre;
            evento.Descripcion = request.Descripcion;
            evento.Ubicacion = request.Ubicacion;
            evento.Fecha = request.FechaInicio;
            evento.Categoria = request.Categoria;

            var docRef = _firestore.Collection(CollectionName).Document(eventId);
            await docRef.SetAsync(evento, SetOptions.MergeAll);

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

        public async Task PublishEventAsync(string eventId, string actorId)
        {
            await GetOwnedEventOrThrowAsync(eventId, actorId);
            var docRef = _firestore.Collection(CollectionName).Document(eventId);
            await docRef.UpdateAsync("Estado", EventStatus.Publicado);
        }

        public async Task CancelEventAsync(string eventId, string actorId)
        {
            await GetOwnedEventOrThrowAsync(eventId, actorId);
            var docRef = _firestore.Collection(CollectionName).Document(eventId);
            await docRef.UpdateAsync("Estado", EventStatus.Cancelado);
        }

        // Lee el evento desde Firestore (nunca confía en un OrganizadorId recibido del cliente)
        // y valida que actorId (UID del token) sea el dueño antes de permitir la operación.
        private async Task<Event> GetOwnedEventOrThrowAsync(string eventId, string actorId)
        {
            var docRef = _firestore.Collection(CollectionName).Document(eventId);
            var snapshot = await docRef.GetSnapshotAsync();
            if (!snapshot.Exists) throw new EventNotFoundException(eventId);

            var evento = snapshot.ConvertTo<Event>();
            if (evento.OrganizadorId != actorId) throw new EventOwnershipException(eventId, actorId);

            return evento;
        }

        public async Task<PagedResponse<Event>> SearchEventsAsync(EventSearchFilterDto filter)
        {
            var query = _firestore.Collection(CollectionName).WhereEqualTo("Estado", (int)EventStatus.Publicado);

            if (!string.IsNullOrEmpty(filter.Categoria) && Enum.TryParse<EventCategory>(filter.Categoria, true, out var parsedCategory))
            {
                query = query.WhereEqualTo("Categoria", (int)parsedCategory);
            }

            if (!string.IsNullOrEmpty(filter.Ubicacion))
            {
                query = query.WhereEqualTo("Ubicacion", filter.Ubicacion);
            }

            // Note: In Firestore, you can only have inequality filters (>, <, >=, <=) on one field.
            if (filter.FechaInicio.HasValue)
            {
                query = query.WhereGreaterThanOrEqualTo("Fecha", filter.FechaInicio.Value.ToUniversalTime());
                query = query.OrderBy("Fecha"); // Requires explicit order matching inequality
            }
            else 
            {
                query = query.OrderBy("Fecha"); // Consistent ordering for pagination
            }

            if (!string.IsNullOrEmpty(filter.LastEventId))
            {
                var cursorDoc = await _firestore.Collection(CollectionName).Document(filter.LastEventId).GetSnapshotAsync();
                if (cursorDoc.Exists)
                {
                    query = query.StartAfter(cursorDoc);
                }
            }

            // Consultar Limit + 1 para saber si hay una página siguiente sin hacer otra query count
            int limit = filter.Limit > 0 ? filter.Limit : 20;
            query = query.Limit(limit + 1);

            var snapshot = await query.GetSnapshotAsync();
            var events = snapshot.Documents.Select(d => d.ConvertTo<Event>()).ToList();

            bool hasNextPage = events.Count > limit;
            if (hasNextPage)
            {
                events.RemoveAt(events.Count - 1); // Remover el "+1" usado solo para comprobación
            }

            return new PagedResponse<Event>
            {
                Data = events,
                LastDocumentId = events.Count > 0 ? events.Last().Id : null,
                HasNextPage = hasNextPage
            };
        }

    }
}
