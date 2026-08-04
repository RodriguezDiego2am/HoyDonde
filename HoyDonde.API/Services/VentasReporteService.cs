using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    // Reporte de ventas simuladas (docs/api-mvp-plan.md §11): filtra por Compra.FechaCompra
    // -cuándo se vendió-, nunca por Event.FechaInicio -cuándo ocurre el evento- (esa es la
    // responsabilidad de ReporteService/ReporteMetricasCalculator, el reporte de desempeño).
    // Lectura no transaccional -es un reporte informativo, no un invariante de negocio-.
    public class VentasReporteService : IVentasReporteService
    {
        private readonly FirestoreDb _firestore;
        private readonly IAuthenticatedPersonaResolver _personaResolver;
        private readonly IEventService _eventService;
        private const string ComprasCollection = "compras";
        private const string TicketsCollection = "tickets";

        // Límite real de Firestore para WhereIn (mismo criterio que ReporteService).
        private const int TicketsWhereInChunkSize = 30;

        public VentasReporteService(FirestoreDb firestore, IAuthenticatedPersonaResolver personaResolver, IEventService eventService)
        {
            _firestore = firestore;
            _personaResolver = personaResolver;
            _eventService = eventService;
        }

        public async Task<VentasReporteResponseDto> GetOrganizerSalesReportAsync(string actorId, VentasOrganizerFilterDto filter)
        {
            var (fechaDesde, fechaHasta) = ReporteFiltroValidator.ValidateRango(filter.FechaDesde, filter.FechaHasta);
            ReporteFiltroValidator.ValidateTicketTypeRequiresEventId(filter.EventId, filter.TicketTypeId);

            var actorPersonaId = await _personaResolver.ResolvePersonaIdAsync(actorId);

            // Ownership del eventId, cuando se solicita explícitamente (docs/api-mvp-plan.md §11):
            // evento inexistente -> 404, evento ajeno -> 403. Nunca se confía en el ownership
            // enviado por el cliente: el Event se re-lee de Firestore y se compara contra el
            // actorPersonaId ya resuelto (mismo criterio que ReporteFiltroValidator.EnsureOwnedEvent).
            if (!string.IsNullOrEmpty(filter.EventId))
            {
                var evento = await _eventService.GetEventEntityByIdAsync(filter.EventId);
                ReporteFiltroValidator.EnsureOwnedEvent(evento, filter.EventId, actorId, actorPersonaId);
                ReporteFiltroValidator.EnsureTicketTypeBelongsToEvent(evento!, filter.EventId, filter.TicketTypeId);
            }

            var comprasBase = await GetComprasAsync(actorPersonaId, fechaDesde, fechaHasta);

            // Eventos disponibles: después de rango/ownership/categoría, ANTES de eventId (docs/api-mvp-plan.md
            // §11.11) -así el selector nunca depende de que ese evento aparezca en el Top 5, y sigue
            // completo aunque ya haya un eventId aplicado, para poder cambiarlo sin limpiar todo-.
            var comprasPostCategoria = ApplyCategoriaFilter(comprasBase, filter.Categoria);
            var eventosDisponibles = BuildEventosDisponibles(comprasPostCategoria);

            var compras = ApplyEventIdFilter(comprasPostCategoria, filter.EventId);

            var ticketsPorCompra = compras.Count == 0
                ? new Dictionary<string, List<Ticket>>()
                : await GetTicketsPorCompraAsync(compras.Select(c => c.Id));

            // Tipos de entrada disponibles: solo con eventId, calculados antes de ticketTypeId
            // (sobre los mismos tickets ya leídos para esas Compras).
            var tiposEntradaDisponibles = string.IsNullOrEmpty(filter.EventId)
                ? new List<VentasTicketTypeOpcionDto>()
                : BuildTiposEntradaDisponibles(compras, ticketsPorCompra);

            if (!string.IsNullOrEmpty(filter.TicketTypeId))
            {
                compras = compras
                    .Where(c => (ticketsPorCompra.TryGetValue(c.Id, out var t) ? t : new List<Ticket>())
                        .Any(tk => tk.TicketTypeId == filter.TicketTypeId))
                    .ToList();
            }

            var resultado = VentasMetricasCalculator.Build(fechaDesde, fechaHasta, compras, ticketsPorCompra, filter.EventId);
            resultado.FiltrosDisponibles = new VentasFiltrosDisponiblesDto { Eventos = eventosDisponibles, TiposEntrada = tiposEntradaDisponibles };
            return resultado;
        }

        public async Task<VentasReporteResponseDto> GetAdminSalesReportAsync(VentasAdminFilterDto filter)
        {
            var (fechaDesde, fechaHasta) = ReporteFiltroValidator.ValidateRango(filter.FechaDesde, filter.FechaHasta);

            var comprasBase = await GetComprasAsync(filter.OrganizadorPersonaId, fechaDesde, fechaHasta);

            var comprasPostCategoria = ApplyCategoriaFilter(comprasBase, filter.Categoria);
            var eventosDisponibles = BuildEventosDisponibles(comprasPostCategoria);

            var compras = ApplyEventIdFilter(comprasPostCategoria, filter.EventId);

            var ticketsPorCompra = compras.Count == 0
                ? new Dictionary<string, List<Ticket>>()
                : await GetTicketsPorCompraAsync(compras.Select(c => c.Id));

            var resultado = VentasMetricasCalculator.Build(fechaDesde, fechaHasta, compras, ticketsPorCompra, filter.EventId);
            // El contrato del reporte de Admin no tiene ticketTypeId (VentasAdminFilterDto): no hay
            // ningún filtro que estas opciones habiliten, así que queda vacío siempre (docs/api-mvp-plan.md §11.11).
            resultado.FiltrosDisponibles = new VentasFiltrosDisponiblesDto { Eventos = eventosDisponibles, TiposEntrada = new List<VentasTicketTypeOpcionDto>() };
            return resultado;
        }

        // Query Firestore compartida (docs/api-mvp-plan.md §11): con organizadorPersonaId ->
        // WhereEqualTo(OrganizadorPersonaId) + rango de FechaCompra (índice compuesto nuevo:
        // compras: OrganizadorPersonaId ASC, FechaCompra ASC); sin organizadorPersonaId -> solo
        // rango de FechaCompra (índice automático de campo simple). Cubre las tres variantes del
        // plan: Organizador (siempre con actor), Admin sin organizador, Admin con organizador.
        private async Task<List<Compra>> GetComprasAsync(string? organizadorPersonaId, DateTime fechaDesde, DateTime fechaHasta)
        {
            Query query = _firestore.Collection(ComprasCollection);

            if (!string.IsNullOrEmpty(organizadorPersonaId))
            {
                query = query.WhereEqualTo(nameof(Compra.OrganizadorPersonaId), organizadorPersonaId);
            }

            query = query
                .WhereGreaterThanOrEqualTo(nameof(Compra.FechaCompra), fechaDesde)
                .WhereLessThan(nameof(Compra.FechaCompra), fechaHasta)
                .OrderBy(nameof(Compra.FechaCompra))
                .OrderBy(FieldPath.DocumentId);

            var snapshot = await query.GetSnapshotAsync();
            return snapshot.Documents.Select(d => d.ConvertTo<Compra>()).ToList();
        }

        // Categoria y eventId se filtran en memoria sobre la query ya acotada por fecha/ownership
        // (docs/api-mvp-plan.md §11), para no requerir índices compuestos adicionales. Separados en
        // dos pasos (a diferencia de la versión anterior, que los aplicaba juntos) porque
        // FiltrosDisponibles.Eventos se calcula entre ambos: después de Categoria, antes de EventId.
        private static List<Compra> ApplyCategoriaFilter(List<Compra> compras, Event.EventCategory? categoria) =>
            categoria.HasValue ? compras.Where(c => c.Categoria == categoria.Value).ToList() : compras;

        private static List<Compra> ApplyEventIdFilter(List<Compra> compras, string? eventId) =>
            string.IsNullOrEmpty(eventId) ? compras : compras.Where(c => c.EventoId == eventId).ToList();

        // Opciones reales para el selector de evento (docs/api-mvp-plan.md §11.11): todos los
        // eventos con Compras en el conjunto ya acotado por rango/ownership/organizador/categoría
        // -nunca solo el Top 5-, únicos por EventoId, ordenados por Nombre y luego Id (desempate
        // determinístico). Nunca expone OrganizadorPersonaId.
        private static List<VentasEventoOpcionDto> BuildEventosDisponibles(IReadOnlyList<Compra> compras) =>
            compras
                .GroupBy(c => (c.EventoId, c.EventoNombre))
                .Select(g => new VentasEventoOpcionDto { Id = g.Key.EventoId, Nombre = g.Key.EventoNombre })
                .OrderBy(e => e.Nombre, StringComparer.Ordinal)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

        // Opciones reales para el selector de tipo de entrada (docs/api-mvp-plan.md §11.11): solo
        // se llama cuando hay eventId, sobre los tickets de las Compras ya acotadas a ese evento
        // (antes de aplicar ticketTypeId). Únicos por TicketTypeId, ordenados por Nombre y luego Id.
        private static List<VentasTicketTypeOpcionDto> BuildTiposEntradaDisponibles(
            IReadOnlyList<Compra> compras, IReadOnlyDictionary<string, List<Ticket>> ticketsPorCompra)
        {
            var compraIds = compras.Select(c => c.Id).ToHashSet();
            return ticketsPorCompra
                .Where(kv => compraIds.Contains(kv.Key))
                .SelectMany(kv => kv.Value)
                .GroupBy(t => (t.TicketTypeId, t.TicketTypeNombre))
                .Select(g => new VentasTicketTypeOpcionDto { Id = g.Key.TicketTypeId, Nombre = g.Key.TicketTypeNombre })
                .OrderBy(t => t.Nombre, StringComparer.Ordinal)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .ToList();
        }

        // Lotes de <=30 ids (límite real de Firestore para WhereIn), nunca una lectura por Compra.
        // Un Ticket sin CompraId (legacy, previo a la etapa "Compra") nunca puede matchear un id de
        // esta lista -son siempre GUIDs de Compra reales- así que queda excluido automáticamente.
        private async Task<Dictionary<string, List<Ticket>>> GetTicketsPorCompraAsync(IEnumerable<string> compraIds)
        {
            var result = new Dictionary<string, List<Ticket>>();

            foreach (var chunk in compraIds.Distinct().Chunk(TicketsWhereInChunkSize))
            {
                var snapshot = await _firestore.Collection(TicketsCollection)
                    .WhereIn(nameof(Ticket.CompraId), chunk)
                    .GetSnapshotAsync();

                foreach (var doc in snapshot.Documents)
                {
                    var ticket = doc.ConvertTo<Ticket>();
                    if (string.IsNullOrEmpty(ticket.CompraId)) continue;

                    if (!result.TryGetValue(ticket.CompraId, out var lista))
                    {
                        lista = new List<Ticket>();
                        result[ticket.CompraId] = lista;
                    }
                    lista.Add(ticket);
                }
            }

            return result;
        }
    }
}
