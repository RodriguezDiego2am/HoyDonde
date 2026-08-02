using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
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
        private readonly IAuthenticatedPersonaResolver _personaResolver;
        private readonly ILogger<EventService> _logger;
        private const string CollectionName = "events";

        public EventService(FirestoreDb firestore, IAuthenticatedPersonaResolver personaResolver, ILogger<EventService> logger)
        {
            _firestore = firestore;
            _personaResolver = personaResolver;
            _logger = logger;
        }

        public async Task<Event?> GetEventEntityByIdAsync(string id)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(id).GetSnapshotAsync();
            if (!snapshot.Exists) return null;
            return snapshot.ConvertTo<Event>();
        }

        public async Task<EventResponse?> GetByIdAsync(string id)
        {
            var evento = await GetEventEntityByIdAsync(id);
            if (evento == null) return null;
            if (!evento.EsPublicamenteVisible(DateTime.UtcNow)) return null;
            return MapToResponse(evento);
        }

        public async Task<EventResponse> GetOwnedByIdAsync(string eventId, string actorId)
        {
            var evento = await GetOwnedEventOrThrowAsync(eventId, actorId);
            return MapToResponse(evento);
        }

        public async Task<IEnumerable<EventResponse>> GetByOrganizerIdAsync(string organizerId)
        {
            var organizadorPersonaId = await _personaResolver.ResolvePersonaIdAsync(organizerId);
            var query = _firestore.Collection(CollectionName).WhereEqualTo(nameof(Event.OrganizadorPersonaId), organizadorPersonaId);
            var snapshot = await query.GetSnapshotAsync();
            return snapshot.Documents.Select(d => MapToResponse(d.ConvertTo<Event>())).ToList();
        }

        public async Task<EventResponse> CreateEventAsync(EventCreateRequest request, string organizerId)
        {
            var organizadorPersonaId = await _personaResolver.ResolvePersonaIdAsync(organizerId);

            _logger.LogInformation("Creating event {Nombre} for organizador persona {PersonaId}", request.Nombre, organizadorPersonaId);

            ValidateForCreate(request);

            var newEvent = new Event
            {
                Id = Guid.NewGuid().ToString(),
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                FechaInicio = request.FechaInicio,
                FechaFin = request.FechaFin,
                Ubicacion = request.Ubicacion,
                Categoria = request.Categoria,
                OrganizadorPersonaId = organizadorPersonaId,
                Estado = EventStatus.Borrador,
            };

            newEvent.TicketTypes = request.TicketGroups.Select(g => new TicketType
            {
                Id = Guid.NewGuid().ToString(),
                Nombre = g.Nombre,
                Precio = g.Precio,
                CantidadDisponible = g.CantidadDisponible,
                EventoId = newEvent.Id
            }).ToList();
            newEvent.CapacidadMaxima = newEvent.TicketTypes.Sum(t => t.CantidadDisponible);

            var docRef = _firestore.Collection(CollectionName).Document(newEvent.Id);
            await docRef.SetAsync(newEvent);

            return MapToResponse(newEvent);
        }

        public async Task<EventResponse> UpdateEventAsync(string eventId, string actorId, EventUpdateRequest request)
        {
            var evento = await GetOwnedEventOrThrowAsync(eventId, actorId);

            if (evento.Estado != EventStatus.Borrador)
                throw new EventNotEditableException(eventId);

            ValidateForUpdate(request);

            // No se toca el ciclo de estados (Estado) ni la propiedad (OrganizadorPersonaId) desde acá.
            evento.Nombre = request.Nombre;
            evento.Descripcion = request.Descripcion;
            evento.Ubicacion = request.Ubicacion;
            evento.FechaInicio = request.FechaInicio;
            evento.FechaFin = request.FechaFin;
            evento.Categoria = request.Categoria;

            // Reemplazo completo de la colección de tipos de ticket (docs/api-mvp-plan.md §2): un
            // evento en Borrador no acepta compras, así que no hay stock vendido que quede huérfano.
            evento.TicketTypes = request.TicketGroups.Select(g => new TicketType
            {
                Id = Guid.NewGuid().ToString(),
                Nombre = g.Nombre,
                Precio = g.Precio,
                CantidadDisponible = g.CantidadDisponible,
                EventoId = evento.Id
            }).ToList();
            evento.CapacidadMaxima = evento.TicketTypes.Sum(t => t.CantidadDisponible);

            var docRef = _firestore.Collection(CollectionName).Document(eventId);
            await docRef.SetAsync(evento, SetOptions.MergeAll);

            return MapToResponse(evento);
        }

        public async Task PublishEventAsync(string eventId, string actorId)
        {
            var evento = await GetOwnedEventOrThrowAsync(eventId, actorId);

            if (evento.Estado != EventStatus.Borrador)
                throw new EventInvalidTransitionException(eventId, evento.Estado.ToString(), EventStatus.Publicado.ToString());

            if (evento.TicketTypes == null || !evento.TicketTypes.Any())
                throw new EventMissingTicketTypesException(eventId);

            var docRef = _firestore.Collection(CollectionName).Document(eventId);
            await docRef.UpdateAsync("Estado", EventStatus.Publicado);
        }

        public async Task CancelEventAsync(string eventId, string actorId)
        {
            var evento = await GetOwnedEventOrThrowAsync(eventId, actorId);

            if (evento.Estado == EventStatus.Cancelado)
                throw new EventInvalidTransitionException(eventId, evento.Estado.ToString(), EventStatus.Cancelado.ToString());

            // Un Publicado cuyo estado efectivo ya es Finalizado es terminal: no admite cancelación
            // (docs/api-mvp-plan.md §1). Se evalúa el estado efectivo, no solo el persistido.
            if (evento.GetEstadoEfectivo(DateTime.UtcNow) == EventEffectiveStatus.Finalizado)
                throw new EventInvalidTransitionException(eventId, EventEffectiveStatus.Finalizado.ToString(), EventStatus.Cancelado.ToString());

            var docRef = _firestore.Collection(CollectionName).Document(eventId);
            await docRef.UpdateAsync("Estado", EventStatus.Cancelado);
        }

        // Lee el evento desde Firestore (nunca confía en un OrganizadorPersonaId recibido del
        // cliente) y valida que el actor autenticado (resuelto a PersonaId) sea el dueño antes
        // de permitir la operación.
        private async Task<Event> GetOwnedEventOrThrowAsync(string eventId, string actorId)
        {
            var organizadorPersonaId = await _personaResolver.ResolvePersonaIdAsync(actorId);

            var evento = await GetEventEntityByIdAsync(eventId);
            if (evento == null) throw new EventNotFoundException(eventId);
            if (evento.OrganizadorPersonaId != organizadorPersonaId) throw new EventOwnershipException(eventId, actorId);

            return evento;
        }

        private static void ValidateForCreate(EventCreateRequest request)
        {
            ValidateCommonFields(request.Nombre, request.Ubicacion, request.FechaInicio, request.FechaFin);

            if (request.TicketGroups == null || !request.TicketGroups.Any())
                throw new EventValidationException("El evento debe tener al menos un tipo de ticket.");

            ValidateTicketGroups(request.TicketGroups);
        }

        private static void ValidateForUpdate(EventUpdateRequest request)
        {
            ValidateCommonFields(request.Nombre, request.Ubicacion, request.FechaInicio, request.FechaFin);

            // A diferencia de la creación, no se exige al menos un tipo de ticket acá: un
            // Borrador puede guardarse sin tipos de ticket, PublishEventAsync es quien lo rechaza
            // (docs/api-mvp-plan.md §2).
            ValidateTicketGroups(request.TicketGroups ?? new List<TicketGroupDto>());
        }

        private static void ValidateCommonFields(string nombre, string ubicacion, DateTime fechaInicio, DateTime fechaFin)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new EventValidationException("El nombre del evento es obligatorio.");

            if (string.IsNullOrWhiteSpace(ubicacion))
                throw new EventValidationException("La ubicación del evento es obligatoria.");

            if (fechaInicio <= DateTime.UtcNow)
                throw new EventValidationException("La fecha de inicio debe ser futura.");

            if (fechaFin <= fechaInicio)
                throw new EventValidationException("La fecha de fin debe ser posterior a la fecha de inicio.");
        }

        private static void ValidateTicketGroups(IEnumerable<TicketGroupDto> groups)
        {
            foreach (var g in groups)
            {
                if (string.IsNullOrWhiteSpace(g.Nombre))
                    throw new EventValidationException("El nombre del tipo de ticket es obligatorio.");

                if (g.Precio < 0)
                    throw new EventValidationException("El precio del tipo de ticket no puede ser negativo.");

                if (g.CantidadDisponible < 1)
                    throw new EventValidationException("La cantidad disponible debe ser al menos 1.");
            }
        }

        private static EventResponse MapToResponse(Event evento)
        {
            return new EventResponse
            {
                Id = evento.Id,
                Nombre = evento.Nombre,
                Descripcion = evento.Descripcion,
                FechaInicio = evento.FechaInicio,
                FechaFin = evento.FechaFin,
                Ubicacion = evento.Ubicacion,
                Categoria = evento.Categoria,
                Estado = evento.GetEstadoEfectivo(DateTime.UtcNow),
                TicketGroups = evento.TicketTypes.Select(t => new TicketGroupDto
                {
                    Nombre = t.Nombre,
                    Precio = t.Precio,
                    CantidadDisponible = t.CantidadDisponible
                }).ToList()
            };
        }

        public async Task<PagedResponse<EventResponse>> SearchEventsAsync(EventSearchFilterDto filter)
        {
            var utcNow = DateTime.UtcNow;

            // Vigencia de catálogo (docs/api-mvp-plan.md §0.1): Publicado y FechaFin todavía no
            // pasó. Firestore admite desigualdades sobre varios campos en una misma consulta
            // (requiere uno de los cuatro índices compuestos de firestore.indexes.json —según
            // qué combinación de Categoria/Ubicacion esté presente—, ver comentario de orderBy
            // más abajo): el filtro opcional por filter.FechaInicio se aplica también del lado
            // de Firestore, nunca en memoria, para no devolver páginas incompletas.
            var query = _firestore.Collection(CollectionName)
                .WhereEqualTo("Estado", (int)EventStatus.Publicado)
                .WhereGreaterThanOrEqualTo("FechaFin", utcNow);

            if (!string.IsNullOrEmpty(filter.Categoria) && Enum.TryParse<EventCategory>(filter.Categoria, true, out var parsedCategory))
            {
                query = query.WhereEqualTo("Categoria", (int)parsedCategory);
            }

            if (!string.IsNullOrEmpty(filter.Ubicacion))
            {
                query = query.WhereEqualTo("Ubicacion", filter.Ubicacion);
            }

            if (filter.FechaInicio.HasValue)
            {
                query = query.WhereGreaterThanOrEqualTo("FechaInicio", filter.FechaInicio.Value.ToUniversalTime());
            }

            // Orden determinístico para paginación: FechaFin (campo del filtro obligatorio),
            // FechaInicio (mismo campo tanto si el filtro opcional está presente como si no, así
            // que una única forma de índice compuesto cubre ambos casos) y el id de documento
            // como desempate final, para que dos eventos con las mismas fechas no queden en un
            // orden ambiguo entre páginas. Cuatro índices compuestos explícitos en
            // firestore.indexes.json cubren las cuatro combinaciones reales de filtros de
            // igualdad opcionales (ninguno / solo Categoria / solo Ubicacion / ambos), todos con
            // el mismo sufijo FechaFin ASC, FechaInicio ASC. __name__ no se declara explícito en
            // el JSON: Firestore lo agrega automáticamente como desempate final en el orden por
            // defecto (ASCENDING), que es exactamente lo que pide OrderBy(FieldPath.DocumentId)
            // acá abajo.
            query = query
                .OrderBy("FechaFin")
                .OrderBy("FechaInicio")
                .OrderBy(FieldPath.DocumentId);

            if (!string.IsNullOrEmpty(filter.LastEventId))
            {
                var cursorDoc = await _firestore.Collection(CollectionName).Document(filter.LastEventId).GetSnapshotAsync();
                if (cursorDoc.Exists)
                {
                    query = query.StartAfter(cursorDoc);
                }
            }

            // Consultar Limit + 1 para saber si hay una página siguiente sin hacer otra query count.
            // El cursor y el límite se aplican después de que todos los filtros (incluido
            // FechaInicio) ya forman parte de la consulta, así que la página nunca queda
            // incompleta ni el cursor se calcula sobre datos filtrados en memoria.
            int limit = filter.Limit > 0 ? filter.Limit : 20;
            query = query.Limit(limit + 1);

            var snapshot = await query.GetSnapshotAsync();
            var events = snapshot.Documents.Select(d => d.ConvertTo<Event>()).ToList();

            bool hasNextPage = events.Count > limit;
            if (hasNextPage)
            {
                events.RemoveAt(events.Count - 1); // Remover el "+1" usado solo para comprobación
            }

            var lastEventId = events.Count > 0 ? events.Last().Id : null;

            return new PagedResponse<EventResponse>
            {
                Data = events.Select(MapToResponse).ToList(),
                LastDocumentId = lastEventId,
                HasNextPage = hasNextPage
            };
        }

    }
}
