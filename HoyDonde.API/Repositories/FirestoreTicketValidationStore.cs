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

        public FirestoreTicketValidationStore(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task<TicketConsumeResult> TryConsumeAsync(string ticketId, string eventId, string validatedByPersonaId)
        {
            var ticketRef = _firestore.Collection(TicketsCollection).Document(ticketId);
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

                if (ticket.Estado == Ticket.TicketStatus.Usado)
                {
                    result = TicketConsumeResult.AlreadyUsed;
                    return;
                }

                if (ticket.Estado == Ticket.TicketStatus.Anulado)
                {
                    result = TicketConsumeResult.Cancelled;
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
