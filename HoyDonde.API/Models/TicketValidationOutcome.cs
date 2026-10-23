namespace HoyDonde.API.Models
{
    public enum TicketValidationOutcome
    {
        Success,
        NotAuthorized,
        // Unknown or stale (rotated by a transfer) QR token.
        QrInvalido,
        AlreadyUsed,
        Anulado,
        EventoCancelado,
        EventoFinalizado
    }
}
