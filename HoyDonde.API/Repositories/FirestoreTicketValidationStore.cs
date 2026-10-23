using System;
using System.Security.Cryptography;
using System.Text;
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

        public async Task<string?> FindEventIdByQrTokenAsync(string qrToken)
        {
            var lookup = await _firestore.Collection(TicketsCollection)
                .WhereEqualTo(nameof(Ticket.QrToken), qrToken)
                .Limit(1)
                .GetSnapshotAsync();
            if (lookup.Count == 0) return null;
            var eventId = lookup.Documents[0].ConvertTo<Ticket>().EventoId;
            return string.IsNullOrEmpty(eventId) ? null : eventId;
        }

        public async Task<TicketConsumeResult> TryConsumeAsync(string qrToken, string eventId, string validatedByPersonaId)
        {
            // The QR carries only the opaque token, so the ticket is located by it. A rotated
            // (stale) token matches no ticket and is rejected exactly like an unknown one.
            var lookup = await _firestore.Collection(TicketsCollection)
                .WhereEqualTo(nameof(Ticket.QrToken), qrToken)
                .Limit(1)
                .GetSnapshotAsync();
            if (lookup.Count == 0)
                return TicketConsumeResult.QrInvalido;

            var ticketRef = lookup.Documents[0].Reference;
            var eventRef = _firestore.Collection(EventsCollection).Document(eventId);
            var result = TicketConsumeResult.QrInvalido;

            await _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(ticketRef);
                if (!snapshot.Exists)
                {
                    result = TicketConsumeResult.QrInvalido;
                    return;
                }

                var ticket = snapshot.ConvertTo<Ticket>();

                // Re-verified inside the transaction: a transfer that rotated the token between
                // the lookup above and this read makes the scanned QR stale.
                if (!TokensMatch(ticket.QrToken, qrToken))
                {
                    result = TicketConsumeResult.QrInvalido;
                    return;
                }

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

        private static bool TokensMatch(string? stored, string presented)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(presented));
        }
    }
}
