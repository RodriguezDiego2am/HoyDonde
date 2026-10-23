using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public enum TicketConsumeResult
    {
        Success,
        // Unknown or stale (rotated by a transfer) QR token.
        QrInvalido,
        EventMismatch,
        AlreadyUsed,
        Anulado,
        EventoCancelado,
        EventoFinalizado
    }

    // Aísla el consumo atómico de un ticket (leer ticket + Event actuales y marcar Usado en
    // una sola transacción) detrás de una interfaz, para que TicketService pueda testearse
    // con un doble en memoria sin depender de un emulador/credenciales de Firestore. La
    // vigencia de validación (docs/api-mvp-plan.md §0.1/§3: Estado == Publicado && UtcNow
    // <= FechaFin) se evalúa contra el Event leído dentro de la misma transacción.
    public interface ITicketValidationStore
    {
        // Event of the ticket that currently owns this QR token; null for an unknown or stale token.
        // Lets the QR carry only the token: the caller derives the event, then checks the Control's
        // assignment against it. TryConsumeAsync still re-verifies everything transactionally.
        Task<string?> FindEventIdByQrTokenAsync(string qrToken);

        Task<TicketConsumeResult> TryConsumeAsync(string qrToken, string eventId, string validatedByPersonaId);
    }
}
