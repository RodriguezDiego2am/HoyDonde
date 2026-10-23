using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;

namespace HoyDonde.API.Tests.Fakes
{
    // In-memory ITicketTransferStore honoring the same contract as FirestoreTicketTransferStore:
    // the whole read-check-write sequence runs under one lock (the real one uses a Firestore
    // transaction), the audit row carries versions only, and nothing is written on rejection.
    public class FakeTicketTransferStore : ITicketTransferStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Ticket> _tickets = new();
        private readonly Dictionary<string, Event> _events = new();

        public List<TicketTransfer> Transfers { get; } = new();

        public List<TicketTransferLookupAttempt> LookupAttempts { get; } = new();

        public void Seed(Ticket ticket) => _tickets[ticket.Id] = ticket;
        public void SeedEvent(Event evento) => _events[evento.Id] = evento;
        public Ticket Get(string ticketId) => _tickets[ticketId];

        public Task<TicketTransferPrecheckResult> PrecheckAndReserveLookupAsync(TicketTransferPrecheckRequest request)
        {
            lock (_gate)
            {
                if (!_tickets.TryGetValue(request.TicketId, out var ticket) || ticket.ClientePersonaId != request.FromPersonaId)
                    return Task.FromResult(new TicketTransferPrecheckResult(TicketTransferStatus.NotFound));

                if (ticket.Estado != Ticket.TicketStatus.Emitido
                    || !_events.TryGetValue(ticket.EventoId, out var evento)
                    || evento.GetEstadoEfectivo(DateTime.UtcNow) != Event.EventEffectiveStatus.Publicado)
                    return Task.FromResult(new TicketTransferPrecheckResult(TicketTransferStatus.NotTransferable));

                var now = DateTime.UtcNow;
                var recent = LookupAttempts.Count(a => a.FromPersonaId == request.FromPersonaId && a.Timestamp >= now - request.Window);
                if (recent >= request.MaxFailedLookupsPerWindow)
                    return Task.FromResult(new TicketTransferPrecheckResult(TicketTransferStatus.RateLimited));

                var attempt = new TicketTransferLookupAttempt { FromPersonaId = request.FromPersonaId, Timestamp = now };
                LookupAttempts.Add(attempt);
                return Task.FromResult(new TicketTransferPrecheckResult(TicketTransferStatus.Success, attempt.Id));
            }
        }

        public Task ReleaseLookupAsync(string lookupReservationId)
        {
            lock (_gate) LookupAttempts.RemoveAll(a => a.Id == lookupReservationId);
            return Task.CompletedTask;
        }

        public Task<TicketTransferResult> TryTransferAsync(TicketTransferRequest request)
        {
            lock (_gate)
            {
                if (!_tickets.TryGetValue(request.TicketId, out var ticket) || ticket.ClientePersonaId != request.FromPersonaId)
                    return Task.FromResult(new TicketTransferResult(TicketTransferStatus.NotFound));

                if (ticket.Estado != Ticket.TicketStatus.Emitido)
                    return Task.FromResult(new TicketTransferResult(TicketTransferStatus.NotTransferable));

                if (!_events.TryGetValue(ticket.EventoId, out var evento)
                    || evento.GetEstadoEfectivo(DateTime.UtcNow) != Event.EventEffectiveStatus.Publicado)
                    return Task.FromResult(new TicketTransferResult(TicketTransferStatus.NotTransferable));

                var now = DateTime.UtcNow;
                var recent = Transfers.Count(t => t.FromPersonaId == request.FromPersonaId && t.Timestamp >= now - request.RateLimitWindow);
                if (recent >= request.MaxTransfersPerWindow)
                    return Task.FromResult(new TicketTransferResult(TicketTransferStatus.RateLimited));

                var previous = ticket.QrVersion;
                var next = Math.Max(previous, 1) + 1;
                ticket.ClientePersonaId = request.ToPersonaId;
                ticket.QrToken = request.NewQrToken;
                ticket.QrVersion = next;

                Transfers.Add(new TicketTransfer
                {
                    TicketId = ticket.Id,
                    EventoId = ticket.EventoId,
                    FromPersonaId = request.FromPersonaId,
                    ToPersonaId = request.ToPersonaId,
                    Timestamp = now,
                    PreviousQrVersion = previous,
                    NewQrVersion = next,
                });

                return Task.FromResult(new TicketTransferResult(TicketTransferStatus.Success, now, next));
            }
        }
    }
}
