using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public enum TicketConsumeResult
    {
        Success,
        NotFound,
        EventMismatch,
        AlreadyUsed,
        Cancelled
    }

    // Aísla el consumo atómico de un ticket (leer estado + marcar Usado en una sola
    // transacción) detrás de una interfaz, para que TicketService pueda testearse con
    // un doble en memoria sin depender de un emulador/credenciales de Firestore.
    public interface ITicketValidationStore
    {
        Task<TicketConsumeResult> TryConsumeAsync(string ticketId, string eventId, string validatedBy);
    }
}
