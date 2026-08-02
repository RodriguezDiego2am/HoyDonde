using System;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public class FirestoreTicketValidationStore : ITicketValidationStore
    {
        private readonly FirestoreDb _firestore;
        private const string TicketsCollection = "tickets";
        private const string EventsCollection = "events";

        public FirestoreTicketValidationStore(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task<TicketConsumeResult> TryConsumeAsync(string ticketId, string eventId, string validatedByPersonaId)
        {
            var ticketRef = _firestore.Collection(TicketsCollection).Document(ticketId);
            var eventRef = _firestore.Collection(EventsCollection).Document(eventId);
            var result = TicketConsumeResult.NotFound;

            await _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(ticketRef);
                if (!snapshot.Exists)
                {
                    result = TicketConsumeResult.NotFound;
                    return;
                }

                var ticket = snapshot.ConvertTo<Ticket>();

                if (ticket.EventoId != eventId)
                {
                    result = TicketConsumeResult.EventMismatch;
                    return;
                }

                // Vigencia de validación (docs/api-mvp-plan.md §0.1/§3): el Event se lee dentro de
                // la misma transacción que intenta consumir el ticket, para que la decisión nunca
                // quede desactualizada frente a una cancelación concurrente.
                var eventSnapshot = await transaction.GetSnapshotAsync(eventRef);
                if (!eventSnapshot.Exists)
                {
                    result = TicketConsumeResult.EventMismatch;
                    return;
                }

                var evento = eventSnapshot.ConvertTo<Event>();
                var estadoEfectivo = evento.GetEstadoEfectivo(DateTime.UtcNow);

                if (estadoEfectivo == Event.EventEffectiveStatus.Finalizado)
                {
                    result = TicketConsumeResult.EventoFinalizado;
                    return;
                }

                if (estadoEfectivo != Event.EventEffectiveStatus.Publicado)
                {
                    result = TicketConsumeResult.EventoCancelado;
                    return;
                }

                if (ticket.Estado == Ticket.TicketStatus.Usado)
                {
                    result = TicketConsumeResult.AlreadyUsed;
                    return;
                }

                if (ticket.Estado == Ticket.TicketStatus.Anulado)
                {
                    result = TicketConsumeResult.Anulado;
                    return;
                }

                ticket.Estado = Ticket.TicketStatus.Usado;
                ticket.FechaUso = DateTime.UtcNow;
                ticket.ValidadoPorPersonaId = validatedByPersonaId;

                transaction.Set(ticketRef, ticket, SetOptions.MergeAll);
                result = TicketConsumeResult.Success;
            });

            return result;
        }
    }
}
