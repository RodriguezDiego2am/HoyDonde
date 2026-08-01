using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
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

            await _firestore.RunTransactionAsync(async transaction =>
            {
                var eventSnapshot = await transaction.GetSnapshotAsync(eventRef);
                if (!eventSnapshot.Exists)
                    throw new Exception("El evento especificado no existe.");

                var evento = eventSnapshot.ConvertTo<Event>();

                if (evento.Estado != Event.EventStatus.Publicado && evento.Estado != Event.EventStatus.Activo)
                    throw new Exception("El evento no admite compra de tickets en este momento.");

                if (evento.TicketTypes == null || !evento.TicketTypes.Any())
                    throw new Exception("El evento no tiene tipos de ticket configurados.");

                var ticketType = evento.TicketTypes.FirstOrDefault(t => t.Id == request.TicketTypeId);
                if (ticketType == null)
                    throw new Exception("El tipo de ticket seleccionado no es válido para este evento.");

                if (ticketType.CantidadDisponible < request.Cantidad)
                    throw new Exception($"Stock insuficiente. Solo quedan {ticketType.CantidadDisponible} tickets disponibles de este tipo.");

                // Deduce stock
                ticketType.CantidadDisponible -= request.Cantidad;

                // Create tickets
                for (int i = 0; i < request.Cantidad; i++)
                {
                    var ticket = new Ticket
                    {
                        Id = Guid.NewGuid().ToString(),
                        TicketTypeId = ticketType.Id,
                        ClientePersonaId = clientePersonaId,
                        EventoId = request.EventoId,
                        FechaCompra = DateTime.UtcNow
                    };

                    var newTicketRef = ticketsColRef.Document(ticket.Id);
                    transaction.Set(newTicketRef, ticket);
                    purchasedTickets.Add(ticket);
                }

                // Update Event in Transaction
                transaction.Set(eventRef, evento, SetOptions.MergeAll);
            });

            _logger.LogInformation("Compra finalizada exitosamente para persona {ClientePersonaId}. Generados {Count} tickets.", clientePersonaId, purchasedTickets.Count);

            return purchasedTickets.Select(t => new TicketResponseDto
            {
                Id = t.Id,
                EventoId = t.EventoId,
                TicketTypeId = t.TicketTypeId,
                ClientePersonaId = t.ClientePersonaId,
                FechaCompra = t.FechaCompra
            }).ToList();
        }

        public async Task<List<TicketResponseDto>> GetTicketsByClienteIdAsync(string clienteId)
        {
            var clientePersonaId = await _personaResolver.ResolvePersonaIdAsync(clienteId);

            var query = _firestore.Collection(TicketsCollection).WhereEqualTo(nameof(Ticket.ClientePersonaId), clientePersonaId);
            var snapshot = await query.GetSnapshotAsync();

            return snapshot.Documents
                .Select(d => d.ConvertTo<Ticket>())
                .Select(t => new TicketResponseDto
                {
                    Id = t.Id,
                    EventoId = t.EventoId,
                    TicketTypeId = t.TicketTypeId,
                    ClientePersonaId = t.ClientePersonaId,
                    FechaCompra = t.FechaCompra
                }).ToList();
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
                TicketConsumeResult.Cancelled => TicketValidationOutcome.Cancelled,
                _ => TicketValidationOutcome.NotFound
            };
        }
    }
}
