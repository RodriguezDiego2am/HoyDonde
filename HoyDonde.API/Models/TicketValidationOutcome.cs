namespace HoyDonde.API.Models
{
    public enum TicketValidationOutcome
    {
        Success,
        NotAuthorized,
        NotFound,
        AlreadyUsed,
        Anulado,
        EventoCancelado,
        EventoFinalizado
    }
}
