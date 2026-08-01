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
        private readonly IUserRepository _userRepository;
        private readonly ITicketValidationStore _validationStore;
        private readonly ILogger<TicketService> _logger;
        private const string TicketsCollection = "tickets";
        private const string EventsCollection = "events";

        public TicketService(
            FirestoreDb firestore,
            IUserRepository userRepository,
            ITicketValidationStore validationStore,
            ILogger<TicketService> logger)
        {
            _firestore = firestore;
            _userRepository = userRepository;
            _validationStore = validationStore;
            _logger = logger;
        }

        public async Task<List<TicketResponseDto>> BuyTicketsAsync(string clienteId, TicketBuyRequest request)
        {
            _logger.LogInformation("Cliente {ClienteId} procesando compra de {Cantidad} tickets para Evento {EventId}", 
                clienteId, request.Cantidad, request.EventoId);

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
                        ClienteId = clienteId,
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

            _logger.LogInformation("Compra finalizada exitosamente para {ClienteId}. Generados {Count} tickets.", clienteId, purchasedTickets.Count);

            return purchasedTickets.Select(t => new TicketResponseDto
            {
                Id = t.Id,
                EventoId = t.EventoId,
                TicketTypeId = t.TicketTypeId,
                ClienteId = t.ClienteId,
                FechaCompra = t.FechaCompra
            }).ToList();
        }

        public async Task<List<TicketResponseDto>> GetTicketsByClienteIdAsync(string clienteId)
        {
            var query = _firestore.Collection(TicketsCollection).WhereEqualTo("ClienteId", clienteId);
            var snapshot = await query.GetSnapshotAsync();

            return snapshot.Documents
                .Select(d => d.ConvertTo<Ticket>())
                .Select(t => new TicketResponseDto
                {
                    Id = t.Id,
                    EventoId = t.EventoId,
                    TicketTypeId = t.TicketTypeId,
                    ClienteId = t.ClienteId,
                    FechaCompra = t.FechaCompra
                }).ToList();
        }

        public async Task<TicketValidationOutcome> ValidateTicketAsync(string controlId, string ticketId, string eventId)
        {
            // El Control y su evento asignado se leen de Firestore; nunca se confía en
            // identificadores de control ni en el eventId "asignado" enviados por el cliente.
            var control = await _userRepository.GetUserByIdAsync(controlId) as Control;
            if (control == null || control.Role != Roles.Control || !control.IsActive)
            {
                _logger.LogWarning("Intento de validación de ticket por usuario no autorizado {ControlId}", controlId);
                return TicketValidationOutcome.NotAuthorized;
            }

            if (control.EventId != eventId)
            {
                _logger.LogWarning(
                    "Control {ControlId} intentó validar tickets del evento {EventId} pero está asignado a {AssignedEventId}",
                    controlId, eventId, control.EventId);
                return TicketValidationOutcome.NotAuthorized;
            }

            var result = await _validationStore.TryConsumeAsync(ticketId, eventId, controlId);

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
