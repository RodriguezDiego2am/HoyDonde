using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;

namespace HoyDonde.API.Tests.Fakes
{
    // Test double for ITicketValidationStore. Mirrors the atomicity guarantee that
    // FirestoreTicketValidationStore gets from Firestore's RunTransactionAsync: the
    // read-check-write sequence for a given ticket happens under a single lock, so two
    // callers racing on the same ticket can never both "win". This does not exercise a
    // real Firestore transaction (no emulator/credentials available here) — it verifies
    // that TicketService's orchestration is correct against a store that honors the same
    // atomicity contract the real implementation is required to provide.
    //
    // Event vigencia (docs/api-mvp-plan.md §0.1/§3) is only evaluated when a matching
    // event was seeded via SeedEvent: tests that only care about ticket-state/assignment
    // behavior (most of TicketServiceValidationTests) never seed an event and keep working
    // as before; tests that need to exercise EventoCancelado/EventoFinalizado seed one.
    public class FakeTicketValidationStore : ITicketValidationStore
    {
        private readonly ConcurrentDictionary<string, Ticket> _tickets = new();
        private readonly ConcurrentDictionary<string, Event> _events = new();
        private readonly ConcurrentDictionary<string, object> _locks = new();

        public void Seed(Ticket ticket) => _tickets[ticket.Id] = ticket;

        public void SeedEvent(Event evento) => _events[evento.Id] = evento;

        public Task<TicketConsumeResult> TryConsumeAsync(string ticketId, string eventId, string validatedByPersonaId)
        {
            var gate = _locks.GetOrAdd(ticketId, _ => new object());

            lock (gate)
            {
                if (!_tickets.TryGetValue(ticketId, out var ticket))
                    return Task.FromResult(TicketConsumeResult.NotFound);

                if (ticket.EventoId != eventId)
                    return Task.FromResult(TicketConsumeResult.EventMismatch);

                if (_events.TryGetValue(eventId, out var evento))
                {
                    var estadoEfectivo = evento.GetEstadoEfectivo(DateTime.UtcNow);

                    if (estadoEfectivo == Event.EventEffectiveStatus.Finalizado)
                        return Task.FromResult(TicketConsumeResult.EventoFinalizado);

                    if (estadoEfectivo != Event.EventEffectiveStatus.Publicado)
                        return Task.FromResult(TicketConsumeResult.EventoCancelado);
                }

                if (ticket.Estado == Ticket.TicketStatus.Usado)
                    return Task.FromResult(TicketConsumeResult.AlreadyUsed);

                if (ticket.Estado == Ticket.TicketStatus.Anulado)
                    return Task.FromResult(TicketConsumeResult.Anulado);

                ticket.Estado = Ticket.TicketStatus.Usado;
                ticket.FechaUso = DateTime.UtcNow;
                ticket.ValidadoPorPersonaId = validatedByPersonaId;

                return Task.FromResult(TicketConsumeResult.Success);
            }
        }
    }
}
