using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Commands
{
    // Explicit command "dotnet run --project HoyDonde.API -- migrate-usuario-email-normalizado".
    // Not an HTTP endpoint: invoked from Program.cs before the server starts, exits when done.
    //
    // Backfills Usuario.EmailNormalizado (trimmed, lowercased Email) so recipients can be found
    // by email regardless of case. Each document is re-checked inside a transaction and only
    // written when the stored value differs, so it is idempotent and safe to re-run, also while
    // the API is serving traffic.
    public class MigrateUsuarioEmailNormalizadoCommand
    {
        private const string UsuariosCollection = "usuarios";
        private const int PageSize = 300;

        private readonly FirestoreDb _firestore;
        private readonly ILogger<MigrateUsuarioEmailNormalizadoCommand> _logger;

        public MigrateUsuarioEmailNormalizadoCommand(FirestoreDb firestore, ILogger<MigrateUsuarioEmailNormalizadoCommand> logger)
        {
            _firestore = firestore;
            _logger = logger;
        }

        public record Summary(int Scanned, int Migrated, int AlreadyNormalized);

        public async Task<int> RunAsync()
        {
            try
            {
                var summary = await MigrateAsync();
                Console.WriteLine(
                    $"Usuarios revisados: {summary.Scanned}. Migrados: {summary.Migrated}. Ya normalizados: {summary.AlreadyNormalized}.");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado normalizando los emails de los usuarios.");
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
                Query query = _firestore.Collection(UsuariosCollection).OrderBy(FieldPath.DocumentId).Limit(PageSize);
                if (last != null)
                    query = query.StartAfter(last);

                var page = await query.GetSnapshotAsync();
                if (page.Count == 0)
                    break;

                foreach (var document in page.Documents)
                {
                    scanned++;
                    if (await TryNormalizeAsync(document.Reference))
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

        private async Task<bool> TryNormalizeAsync(DocumentReference reference)
        {
            var updated = false;
            await _firestore.RunTransactionAsync(async transaction =>
            {
                updated = false;
                var snapshot = await transaction.GetSnapshotAsync(reference);
                if (!snapshot.Exists)
                    return;

                var usuario = snapshot.ConvertTo<Usuario>();
                var expected = Usuario.NormalizarEmail(usuario.Email);
                if (string.Equals(usuario.EmailNormalizado, expected, StringComparison.Ordinal))
                    return;

                transaction.Update(reference, new Dictionary<string, object>
                {
                    [nameof(Usuario.EmailNormalizado)] = expected,
                });
                updated = true;
            });
            return updated;
        }
    }
}
