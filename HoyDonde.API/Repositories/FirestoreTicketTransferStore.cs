using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public class FirestoreTicketTransferStore : ITicketTransferStore
    {
        public const string TransfersCollection = "ticket_transfers";
        public const string LookupAttemptsCollection = "ticket_transfer_lookup_attempts";
        private const string TicketsCollection = "tickets";
        private const string EventsCollection = "events";

        private readonly FirestoreDb _firestore;

        public FirestoreTicketTransferStore(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task<TicketTransferResult> TryTransferAsync(TicketTransferRequest request)
        {
            var ticketRef = _firestore.Collection(TicketsCollection).Document(request.TicketId);
            var result = new TicketTransferResult(TicketTransferStatus.NotFound);

            await _firestore.RunTransactionAsync(async transaction =>
            {
                // Reset on every attempt: RunTransactionAsync may retry the delegate on contention.
                result = new TicketTransferResult(TicketTransferStatus.NotFound);

                var (failure, ticket) = await CheckOwnershipAndStateAsync(transaction, ticketRef, request.FromPersonaId);
                if (failure is not null)
                {
                    result = new TicketTransferResult(failure.Value);
                    return;
                }

                var now = DateTime.UtcNow;
                var windowStart = now - request.RateLimitWindow;
                var recent = await transaction.GetSnapshotAsync(
                    _firestore.Collection(TransfersCollection)
                        .WhereEqualTo(nameof(TicketTransfer.FromPersonaId), request.FromPersonaId)
                        .WhereGreaterThanOrEqualTo(nameof(TicketTransfer.Timestamp), windowStart));
                if (recent.Count >= request.MaxTransfersPerWindow)
                {
                    result = new TicketTransferResult(TicketTransferStatus.RateLimited);
                    return;
                }

                var previousVersion = ticket!.QrVersion;
                var newVersion = Math.Max(previousVersion, 1) + 1;

                transaction.Update(ticketRef, new Dictionary<string, object>
                {
                    [nameof(Ticket.ClientePersonaId)] = request.ToPersonaId,
                    [nameof(Ticket.QrToken)] = request.NewQrToken,
                    [nameof(Ticket.QrVersion)] = newVersion,
                });

                // The audit row carries versions only, never the token itself.
                var audit = new TicketTransfer
                {
                    TicketId = ticket.Id,
                    EventoId = ticket.EventoId,
                    FromPersonaId = request.FromPersonaId,
                    ToPersonaId = request.ToPersonaId,
                    Timestamp = now,
                    PreviousQrVersion = previousVersion,
                    NewQrVersion = newVersion,
                };
                transaction.Set(_firestore.Collection(TransfersCollection).Document(audit.Id), audit);

                result = new TicketTransferResult(TicketTransferStatus.Success, now, newVersion);
            });

            return result;
        }

        public async Task<TicketTransferPrecheckResult> PrecheckAndReserveLookupAsync(TicketTransferPrecheckRequest request)
        {
            var ticketRef = _firestore.Collection(TicketsCollection).Document(request.TicketId);
            var result = new TicketTransferPrecheckResult(TicketTransferStatus.NotFound);

            await _firestore.RunTransactionAsync(async transaction =>
            {
                // Reset on every attempt: RunTransactionAsync may retry the delegate on contention.
                result = new TicketTransferPrecheckResult(TicketTransferStatus.NotFound);

                var (failure, _) = await CheckOwnershipAndStateAsync(transaction, ticketRef, request.FromPersonaId);
                if (failure is not null)
                {
                    result = new TicketTransferPrecheckResult(failure.Value);
                    return;
                }

                var now = DateTime.UtcNow;
                var recent = await transaction.GetSnapshotAsync(
                    _firestore.Collection(LookupAttemptsCollection)
                        .WhereEqualTo(nameof(TicketTransferLookupAttempt.FromPersonaId), request.FromPersonaId)
                        .WhereGreaterThanOrEqualTo(nameof(TicketTransferLookupAttempt.Timestamp), now - request.Window));
                if (recent.Count >= request.MaxFailedLookupsPerWindow)
                {
                    result = new TicketTransferPrecheckResult(TicketTransferStatus.RateLimited);
                    return;
                }

                // Reserved atomically with the count, so a burst of parallel probes cannot all slip
                // under the limit. Only the sender id and a timestamp are stored.
                var attempt = new TicketTransferLookupAttempt { FromPersonaId = request.FromPersonaId, Timestamp = now };
                transaction.Set(_firestore.Collection(LookupAttemptsCollection).Document(attempt.Id), attempt);
                result = new TicketTransferPrecheckResult(TicketTransferStatus.Success, attempt.Id);
            });

            return result;
        }

        public async Task ReleaseLookupAsync(string lookupReservationId) =>
            await _firestore.Collection(LookupAttemptsCollection).Document(lookupReservationId).DeleteAsync();

        // Shared by the pre-check and the authoritative transfer transaction. Reads ticket and event
        // through the given transaction. Returns the failing status (null when transferable).
        private async Task<(TicketTransferStatus? Failure, Ticket? Ticket)> CheckOwnershipAndStateAsync(
            Transaction transaction, DocumentReference ticketRef, string fromPersonaId)
        {
            var ticketSnapshot = await transaction.GetSnapshotAsync(ticketRef);
            if (!ticketSnapshot.Exists)
                return (TicketTransferStatus.NotFound, null);

            var ticket = ticketSnapshot.ConvertTo<Ticket>();
            if (!string.Equals(ticket.ClientePersonaId, fromPersonaId, StringComparison.Ordinal))
                return (TicketTransferStatus.NotFound, null);

            if (ticket.Estado != Ticket.TicketStatus.Emitido)
                return (TicketTransferStatus.NotTransferable, null);

            // Same effective-state rule as validation, read inside the same transaction so a
            // concurrent cancellation can never be raced past.
            var eventSnapshot = await transaction.GetSnapshotAsync(
                _firestore.Collection(EventsCollection).Document(ticket.EventoId));
            if (!eventSnapshot.Exists
                || eventSnapshot.ConvertTo<Event>().GetEstadoEfectivo(DateTime.UtcNow) != Event.EventEffectiveStatus.Publicado)
                return (TicketTransferStatus.NotTransferable, null);

            return (null, ticket);
        }
    }
}
