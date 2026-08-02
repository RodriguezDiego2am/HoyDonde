using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public enum TicketConsumeResult
    {
        Success,
        NotFound,
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
        Task<TicketConsumeResult> TryConsumeAsync(string ticketId, string eventId, string validatedByPersonaId);
    }
}
