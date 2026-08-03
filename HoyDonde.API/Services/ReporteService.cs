using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    // Reporte de solo lectura de eventos propios del Organizador (docs/api-mvp-plan.md §11):
    // orquesta validación (ReporteFiltroValidator), ownership/lecturas de Firestore y agregación
    // (ReporteMetricasCalculator). Lectura no transaccional -es un reporte informativo, no un
    // invariante de negocio-: una compra concurrente durante la generación puede quedar dentro o
    // fuera del resultado según el momento exacto de cada snapshot leído acá.
    public class ReporteService : IReporteService
    {
        private readonly FirestoreDb _firestore;
        private readonly IAuthenticatedPersonaResolver _personaResolver;
        private readonly IEventService _eventService;
        private const string EventsCollection = "events";
        private const string TicketsCollection = "tickets";

        // Límite real de Firestore para WhereIn (docs/api-mvp-plan.md §11.4).
        private const int TicketsWhereInChunkSize = 30;

        public ReporteService(FirestoreDb firestore, IAuthenticatedPersonaResolver personaResolver, IEventService eventService)
        {
            _firestore = firestore;
            _personaResolver = personaResolver;
            _eventService = eventService;
        }

        public async Task<ReporteEventosResponseDto> GetOrganizerEventsReportAsync(string actorId, ReporteEventosFilterDto filter)
        {
            var (fechaDesde, fechaHasta) = ReporteFiltroValidator.ValidateRango(filter.FechaDesde, filter.FechaHasta);
            ReporteFiltroValidator.ValidateTicketTypeRequiresEventId(filter.EventId, filter.TicketTypeId);

            var actorPersonaId = await _personaResolver.ResolvePersonaIdAsync(actorId);
            var utcNow = DateTime.UtcNow;

            var eventos = string.IsNullOrEmpty(filter.EventId)
                ? await GetEventosPropiosEnRangoAsync(actorPersonaId, fechaDesde, fechaHasta, filter, utcNow)
                : await GetEventoPropioSiCumpleAsync(filter.EventId, actorId, actorPersonaId, filter, fechaDesde, fechaHasta, utcNow);

            var ticketsPorEvento = eventos.Count == 0
                ? new Dictionary<string, List<Ticket>>()
                : await GetTicketsPorEventoAsync(eventos.Select(e => e.Id));

            return ReporteMetricasCalculator.Build(fechaDesde, fechaHasta, eventos, ticketsPorEvento, filter.TicketTypeId, utcNow);
        }

        // Camino "con eventId": nunca confía en el ownership del cliente. Un evento propio que cae
        // fuera del rango/estado/categoría pedidos da un reporte vacío, no una fuga de datos.
        private async Task<List<Event>> GetEventoPropioSiCumpleAsync(
            string eventId, string actorId, string actorPersonaId, ReporteEventosFilterDto filter,
            DateTime fechaDesde, DateTime fechaHasta, DateTime utcNow)
        {
            var evento = await _eventService.GetEventEntityByIdAsync(eventId);
            ReporteFiltroValidator.EnsureOwnedEvent(evento, eventId, actorId, actorPersonaId);
            ReporteFiltroValidator.EnsureTicketTypeBelongsToEvent(evento!, eventId, filter.TicketTypeId);

            var cumple = ReporteFiltroValidator.CumpleFiltros(evento!, filter, fechaDesde, fechaHasta, utcNow);
            return cumple ? new List<Event> { evento! } : new List<Event>();
        }

        // Camino "sin eventId": el ownership es siempre parte de la propia consulta Firestore
        // (docs/api-mvp-plan.md §11.4) -WhereEqualTo(OrganizadorPersonaId) + rango de
        // FechaInicio-, nunca solo un filtro en memoria después de leer. Estado/Categoria se
        // aplican después, en memoria, sobre este conjunto ya acotado.
        private async Task<List<Event>> GetEventosPropiosEnRangoAsync(
            string actorPersonaId, DateTime fechaDesde, DateTime fechaHasta, ReporteEventosFilterDto filter, DateTime utcNow)
        {
            var query = _firestore.Collection(EventsCollection)
                .WhereEqualTo(nameof(Event.OrganizadorPersonaId), actorPersonaId)
                .WhereGreaterThanOrEqualTo(nameof(Event.FechaInicio), fechaDesde)
                .WhereLessThan(nameof(Event.FechaInicio), fechaHasta)
                .OrderBy(nameof(Event.FechaInicio))
                .OrderBy(FieldPath.DocumentId);

            var snapshot = await query.GetSnapshotAsync();
            var eventos = snapshot.Documents.Select(d => d.ConvertTo<Event>());

            return eventos
                .Where(e => ReporteFiltroValidator.CumpleFiltros(e, filter, fechaDesde, fechaHasta, utcNow))
                .ToList();
        }

        // Lotes de <=30 ids (límite real de Firestore para WhereIn), nunca una lectura por evento.
        // Si no hay eventos, el llamador no ejecuta ninguna query de tickets.
        private async Task<Dictionary<string, List<Ticket>>> GetTicketsPorEventoAsync(IEnumerable<string> eventIds)
        {
            var result = new Dictionary<string, List<Ticket>>();

            foreach (var chunk in eventIds.Distinct().Chunk(TicketsWhereInChunkSize))
            {
                var snapshot = await _firestore.Collection(TicketsCollection)
                    .WhereIn(nameof(Ticket.EventoId), chunk)
                    .GetSnapshotAsync();

                foreach (var doc in snapshot.Documents)
                {
                    var ticket = doc.ConvertTo<Ticket>();
                    if (!result.TryGetValue(ticket.EventoId, out var lista))
                    {
                        lista = new List<Ticket>();
                        result[ticket.EventoId] = lista;
                    }
                    lista.Add(ticket);
                }
            }

            return result;
        }
    }
}
