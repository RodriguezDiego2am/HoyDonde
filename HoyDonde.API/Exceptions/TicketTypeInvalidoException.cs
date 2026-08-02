using System;

namespace HoyDonde.API.Exceptions
{
    // El TicketTypeId recibido no corresponde a un tipo de ticket del evento indicado
    // (docs/api-mvp-plan.md §3). Se mapea a HTTP 404.
    public class TicketTypeInvalidoException : Exception
    {
        public TicketTypeInvalidoException(string eventId, string ticketTypeId)
            : base($"El tipo de ticket '{ticketTypeId}' no es válido para el evento '{eventId}'.")
        {
        }
    }
}
