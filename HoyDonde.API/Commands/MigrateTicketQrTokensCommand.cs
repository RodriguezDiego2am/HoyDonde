using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Commands
{
    // Explicit command "dotnet run --project HoyDonde.API -- migrate-ticket-qr-tokens".
    // Not an HTTP endpoint: invoked from Program.cs before the server starts, exits when done.
    //
    // Assigns a fresh random QR token (QrVersion = 1) to every ticket that lacks one, i.e. tickets
    // issued before ticket transfers existed. It only ever touches tickets with an empty QrToken
    // and re-checks that inside a per-document transaction, so it is idempotent, safe to re-run,
    // and safe to run while the API is serving traffic (a ticket that got a token in the meantime
    // is skipped, never overwritten). Run it BEFORE the door staff scan the new QR format: from
    // this release on the legacy `{ticketId,eventId}` QR is rejected by validation.
    public class MigrateTicketQrTokensCommand
    {
        private const string TicketsCollection = "tickets";
        private const int PageSize = 300;

        private readonly FirestoreDb _firestore;
        private readonly ILogger<MigrateTicketQrTokensCommand> _logger;

        public MigrateTicketQrTokensCommand(FirestoreDb firestore, ILogger<MigrateTicketQrTokensCommand> logger)
        {
            _firestore = firestore;
            _logger = logger;
        }

        public record Summary(int Scanned, int Migrated, int AlreadyHadToken);

        public async Task<int> RunAsync()
        {
            try
            {
                var summary = await MigrateAsync();
                Console.WriteLine(
                    $"Tickets revisados: {summary.Scanned}. Migrados: {summary.Migrated}. Ya tenían token: {summary.AlreadyHadToken}.");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado migrando los tokens QR de los tickets.");
                Console.Error.WriteLine("Error inesperado. Revise los logs para más detalle. Es seguro volver a ejecutar el comando.");
                return 1;
            }
        }

        public async Task<Summary> MigrateAsync()
        {
            var scanned = 0;
            var migrated = 0;
            var already = 0;
            DocumentSnapshot? last = null;

            while (true)
            {
                Query query = _firestore.Collection(TicketsCollection).OrderBy(FieldPath.DocumentId).Limit(PageSize);
                if (last != null)
                    query = query.StartAfter(last);

                var page = await query.GetSnapshotAsync();
                if (page.Count == 0)
                    break;

                foreach (var document in page.Documents)
                {
                    scanned++;
                    if (await TryAssignTokenAsync(document.Reference))
                        migrated++;
                    else
                        already++;
                }

                last = page.Documents[page.Count - 1];
                if (page.Count < PageSize)
                    break;
            }

            return new Summary(scanned, migrated, already);
        }

        private async Task<bool> TryAssignTokenAsync(DocumentReference reference)
        {
            var assigned = false;
            await _firestore.RunTransactionAsync(async transaction =>
            {
                assigned = false;
                var snapshot = await transaction.GetSnapshotAsync(reference);
                if (!snapshot.Exists)
                    return;

                var ticket = snapshot.ConvertTo<Ticket>();
                if (!string.IsNullOrEmpty(ticket.QrToken))
                    return;

                transaction.Update(reference, new Dictionary<string, object>
                {
                    [nameof(Ticket.QrToken)] = QrTokenGenerator.NewToken(),
                    [nameof(Ticket.QrVersion)] = 1,
                });
                assigned = true;
            });
            return assigned;
        }
    }
}
