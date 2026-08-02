using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Services
{
    public class TicketService : ITicketService
    {
        private readonly FirestoreDb _firestore;
        private readonly IAuthenticatedPersonaResolver _personaResolver;
        private readonly IControlAsignacionRepository _controlAsignacionRepository;
        private readonly ITicketValidationStore _validationStore;
        private readonly ILogger<TicketService> _logger;
        private const string TicketsCollection = "tickets";
        private const string EventsCollection = "events";

        public TicketService(
            FirestoreDb firestore,
            IAuthenticatedPersonaResolver personaResolver,
            IControlAsignacionRepository controlAsignacionRepository,
            ITicketValidationStore validationStore,
            ILogger<TicketService> logger)
        {
            _firestore = firestore;
            _personaResolver = personaResolver;
            _controlAsignacionRepository = controlAsignacionRepository;
            _validationStore = validationStore;
            _logger = logger;
        }

        public async Task<List<TicketResponseDto>> BuyTicketsAsync(string clienteId, TicketBuyRequest request)
        {
            var clientePersonaId = await _personaResolver.ResolvePersonaIdAsync(clienteId);

            _logger.LogInformation("Cliente persona {ClientePersonaId} procesando compra de {Cantidad} tickets para Evento {EventId}",
                clientePersonaId, request.Cantidad, request.EventoId);

            var eventRef = _firestore.Collection(EventsCollection).Document(request.EventoId);
            var ticketsColRef = _firestore.Collection(TicketsCollection);
            var purchasedTickets = new List<Ticket>();
            Event evento = null!;

            await _firestore.RunTransactionAsync(async transaction =>
            {
                // El Event se lee dentro de la misma transacción que descuenta stock y crea los
                // tickets (docs/api-mvp-plan.md §3): precio, nombres y vigencia salen exclusivamente
                // de esta lectura, nunca de lo que envía el cliente.
                var eventSnapshot = await transaction.GetSnapshotAsync(eventRef);
                if (!eventSnapshot.Exists)
                    throw new EventNotFoundException(request.EventoId);

                evento = eventSnapshot.ConvertTo<Event>();
                var utcNow = DateTime.UtcNow;

                // Vigencia de compra (docs/api-mvp-plan.md §0.1): Publicado y todavía no empezó.
                if (evento.Estado != Event.EventStatus.Publicado || utcNow >= evento.FechaInicio)
                    throw new EventoNoDisponibleParaCompraException(request.EventoId);

                var ticketType = evento.TicketTypes?.FirstOrDefault(t => t.Id == request.TicketTypeId);
                if (ticketType == null)
                    throw new TicketTypeInvalidoException(request.EventoId, request.TicketTypeId);

                if (ticketType.CantidadDisponible < request.Cantidad)
                    throw new StockInsuficienteException(request.TicketTypeId, ticketType.CantidadDisponible, request.Cantidad);

                // Deduce stock
                ticketType.CantidadDisponible -= request.Cantidad;

                // Create tickets, fotografiando Event/TicketType en este mismo instante.
                for (int i = 0; i < request.Cantidad; i++)
                {
                    var ticket = new Ticket
                    {
                        Id = Guid.NewGuid().ToString(),
                        TicketTypeId = ticketType.Id,
                        ClientePersonaId = clientePersonaId,
                        EventoId = request.EventoId,
                        FechaCompra = utcNow,
                        Estado = Ticket.TicketStatus.Emitido,
                        EventoNombre = evento.Nombre,
                        TicketTypeNombre = ticketType.Nombre,
                        PrecioPagado = ticketType.Precio,
                        FechaInicio = evento.FechaInicio,
                        FechaFin = evento.FechaFin,
                    };

                    var newTicketRef = ticketsColRef.Document(ticket.Id);
                    transaction.Set(newTicketRef, ticket);
                    purchasedTickets.Add(ticket);
                }

                // Update Event in Transaction
                transaction.Set(eventRef, evento, SetOptions.MergeAll);
            });

            _logger.LogInformation("Compra finalizada exitosamente para persona {ClientePersonaId}. Generados {Count} tickets.", clientePersonaId, purchasedTickets.Count);

            return purchasedTickets.Select(t => MapToResponse(t, evento)).ToList();
        }

        public async Task<List<TicketResponseDto>> GetTicketsByClienteIdAsync(string clienteId)
        {
            var clientePersonaId = await _personaResolver.ResolvePersonaIdAsync(clienteId);

            var query = _firestore.Collection(TicketsCollection).WhereEqualTo(nameof(Ticket.ClientePersonaId), clientePersonaId);
            var snapshot = await query.GetSnapshotAsync();
            var tickets = snapshot.Documents.Select(d => d.ConvertTo<Ticket>()).ToList();

            // Utilizable/MotivoNoUtilizable dependen del Event actual (FechaInicio/FechaFin, en
            // cambio, salen de la fotografía persistida en cada Ticket, no de esta lectura — ver
            // MapToResponse). Se agrupa por EventoId y se hace una única lectura batch por
            // identificador distinto, en vez de una lectura por ticket.
            var eventosPorId = await GetEventosByIdsAsync(tickets.Select(t => t.EventoId));

            return tickets
                .Select(t => MapToResponse(t, eventosPorId.GetValueOrDefault(t.EventoId)))
                .ToList();
        }

        public async Task<TicketValidationOutcome> ValidateTicketAsync(string controlId, string ticketId, string eventId)
        {
            // El Control se resuelve a su PersonaId y su asignación se lee de Firestore; nunca se
            // confía en un eventId "asignado" enviado por el cliente.
            var controlPersonaId = await _personaResolver.ResolvePersonaIdAsync(controlId);

            var asignado = await _controlAsignacionRepository.ExisteAsignacionAsync(controlPersonaId, eventId);
            if (!asignado)
            {
                _logger.LogWarning(
                    "Control persona {ControlPersonaId} intentó validar tickets del evento {EventId} sin asignación vigente.",
                    controlPersonaId, eventId);
                return TicketValidationOutcome.NotAuthorized;
            }

            var result = await _validationStore.TryConsumeAsync(ticketId, eventId, controlPersonaId);

            return result switch
            {
                TicketConsumeResult.Success => TicketValidationOutcome.Success,
                TicketConsumeResult.EventMismatch => TicketValidationOutcome.NotAuthorized,
                TicketConsumeResult.AlreadyUsed => TicketValidationOutcome.AlreadyUsed,
                TicketConsumeResult.Anulado => TicketValidationOutcome.Anulado,
                TicketConsumeResult.EventoCancelado => TicketValidationOutcome.EventoCancelado,
                TicketConsumeResult.EventoFinalizado => TicketValidationOutcome.EventoFinalizado,
                _ => TicketValidationOutcome.NotFound
            };
        }

        private async Task<Dictionary<string, Event>> GetEventosByIdsAsync(IEnumerable<string> eventIds)
        {
            var distinctIds = eventIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (distinctIds.Count == 0)
                return new Dictionary<string, Event>();

            var refs = distinctIds.Select(id => _firestore.Collection(EventsCollection).Document(id));
            var snapshots = await _firestore.GetAllSnapshotsAsync(refs);

            return snapshots
                .Where(s => s.Exists)
                .Select(s => s.ConvertTo<Event>())
                .ToDictionary(e => e.Id);
        }

        // Estado histórico del ticket + Utilizable/MotivoNoUtilizable derivados del Event actual
        // (docs/api-mvp-plan.md §3). `evento` es null solo en el caso defensivo de un ticket cuyo
        // Event referenciado ya no existe; se trata igual que un evento cancelado. FechaInicio y
        // FechaFin, en cambio, NUNCA salen de `evento`: son parte de la fotografía inmutable
        // tomada al comprar (Ticket.FechaInicio/FechaFin), para no reescribir el historial si el
        // Event cambiara.
        private static TicketResponseDto MapToResponse(Ticket ticket, Event? evento)
        {
            string? motivo;
            bool utilizable;

            if (ticket.Estado == Ticket.TicketStatus.Usado)
            {
                utilizable = false;
                motivo = "Usado";
            }
            else if (ticket.Estado == Ticket.TicketStatus.Anulado)
            {
                utilizable = false;
                motivo = "Anulado";
            }
            else if (evento == null)
            {
                utilizable = false;
                motivo = "EventoCancelado";
            }
            else
            {
                var estadoEfectivo = evento.GetEstadoEfectivo(DateTime.UtcNow);
                if (estadoEfectivo == Event.EventEffectiveStatus.Finalizado)
                {
                    utilizable = false;
                    motivo = "EventoFinalizado";
                }
                else if (estadoEfectivo != Event.EventEffectiveStatus.Publicado)
                {
                    utilizable = false;
                    motivo = "EventoCancelado";
                }
                else
                {
                    utilizable = true;
                    motivo = null;
                }
            }

            return new TicketResponseDto
            {
                Id = ticket.Id,
                EventoId = ticket.EventoId,
                TicketTypeId = ticket.TicketTypeId,
                ClientePersonaId = ticket.ClientePersonaId,
                FechaCompra = ticket.FechaCompra,
                Estado = ticket.Estado.ToString(),
                Utilizable = utilizable,
                MotivoNoUtilizable = motivo,
                EventoNombre = ticket.EventoNombre,
                TicketTypeNombre = ticket.TicketTypeNombre,
                PrecioPagado = ticket.PrecioPagado,
                FechaInicio = ticket.FechaInicio,
                FechaFin = ticket.FechaFin,
            };
        }
    }
}
