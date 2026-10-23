using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public sealed class PurchaseReservationRequest
    {
        public string ClientePersonaId { get; init; } = string.Empty;
        public string EventoId { get; init; } = string.Empty;
        public string TicketTypeId { get; init; } = string.Empty;
        public int Cantidad { get; init; }
        public string? IdempotencyKey { get; init; }
    }

    // Provider-independent reservation foundation. No HTTP, webhook, worker, or preference
    // creation belongs here; callers may only pass an already verified approval timestamp.
    public class PurchaseService
    {
        private const string EventsCollection = "events";
        private const string PurchasesCollection = "compras";
        private const string TicketsCollection = "tickets";
        private const string AuditCollection = "purchase_audit";
        private readonly FirestoreDb _firestore;

        public PurchaseService(FirestoreDb firestore)
        {
            _firestore = firestore ?? throw new ArgumentNullException(nameof(firestore));
        }

        public Task<Compra> ReserveAsync(PurchaseReservationRequest request, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.ClientePersonaId) || string.IsNullOrWhiteSpace(request.EventoId) ||
                string.IsNullOrWhiteSpace(request.TicketTypeId) || request.Cantidad <= 0)
                throw new ArgumentException("La reserva requiere cliente, evento, tipo de entrada y cantidad positiva.", nameof(request));

            var reservedAt = ToUtc(utcNow);
            var compraId = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.ClientePersonaId}:{request.IdempotencyKey}"))).ToLowerInvariant();
            var purchaseRef = _firestore.Collection(PurchasesCollection).Document(compraId);
            var eventRef = _firestore.Collection(EventsCollection).Document(request.EventoId);

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var existingPurchase = await transaction.GetSnapshotAsync(purchaseRef);
                if (existingPurchase.Exists)
                    return existingPurchase.ConvertTo<Compra>();

                var eventSnapshot = await transaction.GetSnapshotAsync(eventRef);
                if (!eventSnapshot.Exists)
                    throw new EventNotFoundException(request.EventoId);

                var evento = eventSnapshot.ConvertTo<Event>();
                if (evento.Estado != Event.EventStatus.Publicado || reservedAt >= evento.FechaInicio)
                    throw new EventoNoDisponibleParaCompraException(request.EventoId);

                var ticketType = evento.TicketTypes?.SingleOrDefault(type => type.Id == request.TicketTypeId);
                if (ticketType is null)
                    throw new TicketTypeInvalidoException(request.EventoId, request.TicketTypeId);
                if (ticketType.CantidadDisponible < request.Cantidad)
                    throw new StockInsuficienteException(request.TicketTypeId, ticketType.CantidadDisponible, request.Cantidad);

                ticketType.CantidadDisponible -= request.Cantidad;
                ticketType.CantidadReservada += request.Cantidad;
                evento.ReservasActivas += request.Cantidad;

                var compra = PurchaseStateMachine.CreatePending(reservedAt);
                compra.Id = compraId;
                compra.ClientePersonaId = request.ClientePersonaId;
                compra.EventoId = request.EventoId;
                compra.FechaCompra = reservedAt;
                compra.CantidadEntradas = request.Cantidad;
                compra.ImporteTotal = ticketType.Precio * request.Cantidad;
                compra.TicketTypeId = ticketType.Id;
                compra.TicketTypeNombre = ticketType.Nombre;
                compra.Moneda = "ARS";
                compra.PurchaseIdempotencyKeyHash = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.IdempotencyKey))).ToLowerInvariant();
                compra.EventoNombre = evento.Nombre;
                compra.Ubicacion = evento.Ubicacion;
                compra.FechaInicio = evento.FechaInicio;
                compra.FechaFin = evento.FechaFin;
                compra.OrganizadorPersonaId = evento.OrganizadorPersonaId;
                compra.Categoria = evento.Categoria;

                transaction.Set(purchaseRef, compra);
                transaction.Set(eventRef, evento, SetOptions.MergeAll);
                AddAudit(transaction, compraId, reservedAt, "ReservationCreated", "Active");
                return compra;
            });
        }

        public Task<PurchaseTransition> ExpireAsync(string compraId, DateTime utcNow)
        {
            var purchaseRef = _firestore.Collection(PurchasesCollection).Document(compraId);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(purchaseRef);
                if (!snapshot.Exists)
                    throw new InvalidOperationException($"La compra '{compraId}' no existe.");

                var compra = snapshot.ConvertTo<Compra>();
                if (!PurchaseStateMachine.Expire(compra, utcNow))
                    return PurchaseTransition.NoChange;

                var eventRef = _firestore.Collection(EventsCollection).Document(compra.EventoId);
                var eventSnapshot = await transaction.GetSnapshotAsync(eventRef);
                if (!eventSnapshot.Exists)
                    throw new InvalidOperationException($"El evento '{compra.EventoId}' no existe para liberar la reserva.");

                var evento = eventSnapshot.ConvertTo<Event>();
                ReleaseInventory(evento, compra);
                transaction.Set(eventRef, evento, SetOptions.MergeAll);
                transaction.Set(purchaseRef, compra, SetOptions.MergeAll);
                AddAudit(transaction, compra.Id, ToUtc(utcNow), "ReservationReleased", "Expired");
                return PurchaseTransition.Expired;
            });
        }

        public Task ProcessNotApprovedAsync(string compraId, DateTime utcNow)
        {
            var purchaseRef = _firestore.Collection(PurchasesCollection).Document(compraId);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(purchaseRef);
                if (!snapshot.Exists) throw new InvalidOperationException($"La compra '{compraId}' no existe.");
                var compra = snapshot.ConvertTo<Compra>();
                if (!PurchaseStateMachine.ApplyNotApproved(compra)) return;
                var eventRef = _firestore.Collection(EventsCollection).Document(compra.EventoId);
                var eventSnapshot = await transaction.GetSnapshotAsync(eventRef);
                if (!eventSnapshot.Exists) throw new InvalidOperationException($"El evento '{compra.EventoId}' no existe para liberar la reserva.");
                var evento = eventSnapshot.ConvertTo<Event>();
                ReleaseInventory(evento, compra);
                transaction.Set(eventRef, evento, SetOptions.MergeAll);
                transaction.Set(purchaseRef, compra, SetOptions.MergeAll);
                AddAudit(transaction, compra.Id, ToUtc(utcNow), "ReservationReleased", "PaymentNotApproved");
            });
        }

        // Free tickets retain the local direct-confirmation flow: their price is derived in the
        // reservation transaction, so no caller-controlled zero-price flag can bypass Checkout Pro.
        public async Task<PurchaseTransition> ConfirmFreePurchaseAsync(string compraId, DateTime utcNow)
        {
            var snapshot = await _firestore.Collection(PurchasesCollection).Document(compraId).GetSnapshotAsync();
            if (!snapshot.Exists) throw new InvalidOperationException($"La compra '{compraId}' no existe.");
            if (snapshot.ConvertTo<Compra>().ImporteTotal != 0m)
                throw new InvalidOperationException("Solo una compra con importe cero puede confirmarse directamente.");
            return await ProcessVerifiedApprovalAsync(compraId, utcNow);
        }

        public Task<PurchaseTransition> ProcessVerifiedApprovalAsync(string compraId, DateTime approvedAt)
        {
            var purchaseRef = _firestore.Collection(PurchasesCollection).Document(compraId);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(purchaseRef);
                if (!snapshot.Exists)
                    throw new InvalidOperationException($"La compra '{compraId}' no existe.");

                var compra = snapshot.ConvertTo<Compra>();
                var wasActive = compra.EstadoReserva == Compra.ReservationStatus.Active;
                var transition = PurchaseStateMachine.ApplyVerifiedApproval(compra, approvedAt);
                if (transition == PurchaseTransition.NoChange)
                    return transition;

                var eventRef = _firestore.Collection(EventsCollection).Document(compra.EventoId);
                var eventSnapshot = await transaction.GetSnapshotAsync(eventRef);
                if (!eventSnapshot.Exists)
                    throw new InvalidOperationException($"El evento '{compra.EventoId}' no existe para resolver la reserva.");

                var evento = eventSnapshot.ConvertTo<Event>();
                if (transition == PurchaseTransition.Confirmed)
                {
                    ConsumeInventory(evento, compra);
                    CreateTickets(transaction, compra);
                    AddAudit(transaction, compra.Id, ToUtc(approvedAt), "ReservationConsumed", "Confirmed");
                }
                else if (transition == PurchaseTransition.RefundRequired && wasActive)
                {
                    ReleaseInventory(evento, compra);
                    AddAudit(transaction, compra.Id, ToUtc(approvedAt), "ReservationReleased", compra.MotivoRefund.ToString());
                }
                else
                {
                    AddAudit(transaction, compra.Id, ToUtc(approvedAt), "RefundRequired", compra.MotivoRefund.ToString());
                }

                transaction.Set(eventRef, evento, SetOptions.MergeAll);
                transaction.Set(purchaseRef, compra, SetOptions.MergeAll);
                return transition;
            });
        }

        private void CreateTickets(Transaction transaction, Compra compra)
        {
            for (var index = 0; index < compra.CantidadEntradas; index++)
            {
                var ticket = new Ticket
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CompraId = compra.Id,
                    TicketTypeId = compra.TicketTypeId,
                    ClientePersonaId = compra.ClientePersonaId,
                    FechaCompra = compra.FechaCompra,
                    EventoId = compra.EventoId,
                    Estado = Ticket.TicketStatus.Emitido,
                    EventoNombre = compra.EventoNombre,
                    TicketTypeNombre = compra.TicketTypeNombre,
                    PrecioPagado = compra.ImporteTotal / compra.CantidadEntradas,
                    FechaInicio = compra.FechaInicio,
                    FechaFin = compra.FechaFin,
                    QrToken = QrTokenGenerator.NewToken(),
                    QrVersion = 1,
                };
                transaction.Set(_firestore.Collection(TicketsCollection).Document(ticket.Id), ticket);
            }
        }

        private static void ReleaseInventory(Event evento, Compra compra)
        {
            var ticketType = GetTicketType(evento, compra);
            RequireReservedInventory(evento, ticketType, compra);
            ticketType.CantidadDisponible += compra.CantidadEntradas;
            ticketType.CantidadReservada -= compra.CantidadEntradas;
            evento.ReservasActivas -= compra.CantidadEntradas;
        }

        private static void ConsumeInventory(Event evento, Compra compra)
        {
            var ticketType = GetTicketType(evento, compra);
            RequireReservedInventory(evento, ticketType, compra);
            ticketType.CantidadReservada -= compra.CantidadEntradas;
            evento.ReservasActivas -= compra.CantidadEntradas;
        }

        private static TicketType GetTicketType(Event evento, Compra compra) =>
            evento.TicketTypes?.SingleOrDefault(type => type.Id == compra.TicketTypeId)
                ?? throw new InvalidOperationException($"El tipo '{compra.TicketTypeId}' no existe para la compra '{compra.Id}'.");

        private static void RequireReservedInventory(Event evento, TicketType ticketType, Compra compra)
        {
            if (evento.ReservasActivas < compra.CantidadEntradas || ticketType.CantidadReservada < compra.CantidadEntradas)
                throw new InvalidOperationException($"La reserva activa de la compra '{compra.Id}' no concuerda con el inventario.");
        }

        private void AddAudit(Transaction transaction, string compraId, DateTime occurredAt, string action, string sanitizedReason)
        {
            var entry = new PurchaseAuditEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                CompraId = compraId,
                OccurredAt = occurredAt,
                Action = action,
                SanitizedReason = sanitizedReason,
            };
            transaction.Set(_firestore.Collection(AuditCollection).Document(entry.Id), entry);
        }

        private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }
}
