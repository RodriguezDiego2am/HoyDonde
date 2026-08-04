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

        // GET /api/reports/admin/events (docs/api-mvp-plan.md §11.3): sin ownership de un actor
        // -es un reporte global-, OrganizadorPersonaId es un filtro opcional y arbitrario que
        // viene directo del cliente. Reutiliza ReporteMetricasCalculator (misma agregación que el
        // reporte del Organizador) y solo agrega OrganizadorPersonaId a cada fila después.
        public async Task<ReporteAdminEventosResponseDto> GetAdminEventsReportAsync(ReporteAdminEventosFilterDto filter)
        {
            var (fechaDesde, fechaHasta) = ReporteFiltroValidator.ValidateRango(filter.FechaDesde, filter.FechaHasta);
            var utcNow = DateTime.UtcNow;

            var eventos = await GetEventosEnRangoAsync(fechaDesde, fechaHasta, filter, utcNow);

            var ticketsPorEvento = eventos.Count == 0
                ? new Dictionary<string, List<Ticket>>()
                : await GetTicketsPorEventoAsync(eventos.Select(e => e.Id));

            var baseReporte = ReporteMetricasCalculator.Build(fechaDesde, fechaHasta, eventos, ticketsPorEvento, ticketTypeId: null, utcNow);

            return new ReporteAdminEventosResponseDto
            {
                FechaDesde = baseReporte.FechaDesde,
                FechaHasta = baseReporte.FechaHasta,
                AclaracionImporte = baseReporte.AclaracionImporte,
                Resumen = baseReporte.Resumen,
                Destacados = baseReporte.Destacados,
                // El orden de baseReporte.Eventos preserva el orden de 'eventos' (ReporteMetricasCalculator.Build
                // itera con Select sobre la misma lista), así que el zip por índice es seguro.
                Eventos = eventos.Zip(baseReporte.Eventos, ToAdminDetalle).ToList(),
            };
        }

        private static ReporteAdminEventoDetalleDto ToAdminDetalle(Event evento, ReporteEventoDetalleDto detalle) => new()
        {
            EventId = detalle.EventId,
            Nombre = detalle.Nombre,
            Ubicacion = detalle.Ubicacion,
            Categoria = detalle.Categoria,
            Estado = detalle.Estado,
            FechaInicio = detalle.FechaInicio,
            FechaFin = detalle.FechaFin,
            CapacidadInicial = detalle.CapacidadInicial,
            StockDisponible = detalle.StockDisponible,
            EntradasEmitidas = detalle.EntradasEmitidas,
            EntradasUsadas = detalle.EntradasUsadas,
            EntradasAnuladas = detalle.EntradasAnuladas,
            EntradasPendientes = detalle.EntradasPendientes,
            PorcentajeOcupacion = detalle.PorcentajeOcupacion,
            PorcentajeAsistencia = detalle.PorcentajeAsistencia,
            PorcentajeUtilizacion = detalle.PorcentajeUtilizacion,
            ImporteEmitido = detalle.ImporteEmitido,
            EntradasNoUtilizadas = detalle.EntradasNoUtilizadas,
            PorcentajeNoUtilizacion = detalle.PorcentajeNoUtilizacion,
            TiposDeEntrada = detalle.TiposDeEntrada,
            OrganizadorPersonaId = evento.OrganizadorPersonaId,
        };

        // Estrategia Firestore (docs/api-mvp-plan.md §11.4): sin organizadorPersonaId, solo rango
        // de FechaInicio (índice automático de campo simple); con organizadorPersonaId, se agrega
        // WhereEqualTo -mismo índice compuesto que el reporte del Organizador-. Estado/Categoria
        // siempre en memoria.
        private async Task<List<Event>> GetEventosEnRangoAsync(DateTime fechaDesde, DateTime fechaHasta, ReporteAdminEventosFilterDto filter, DateTime utcNow)
        {
            Query query = _firestore.Collection(EventsCollection);

            if (!string.IsNullOrEmpty(filter.OrganizadorPersonaId))
            {
                query = query.WhereEqualTo(nameof(Event.OrganizadorPersonaId), filter.OrganizadorPersonaId);
            }

            query = query
                .WhereGreaterThanOrEqualTo(nameof(Event.FechaInicio), fechaDesde)
                .WhereLessThan(nameof(Event.FechaInicio), fechaHasta)
                .OrderBy(nameof(Event.FechaInicio))
                .OrderBy(FieldPath.DocumentId);

            var snapshot = await query.GetSnapshotAsync();
            var eventos = snapshot.Documents.Select(d => d.ConvertTo<Event>());

            return eventos
                .Where(e => ReporteFiltroValidator.CumpleFiltros(e, filter.Estado, filter.Categoria, fechaDesde, fechaHasta, utcNow))
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
