using System;

namespace HoyDonde.API.Exceptions
{
    // PublishEventAsync exige al menos un tipo de ticket (docs/api-mvp-plan.md §2). Se mapea a
    // HTTP 409: los datos guardados eran válidos al momento de guardarlos, el problema es de
    // estado (se editó el Borrador dejando la colección de tipos de ticket vacía).
    public class EventMissingTicketTypesException : Exception
    {
        public EventMissingTicketTypesException(string eventId)
            : base($"El evento '{eventId}' no tiene tipos de ticket y no puede publicarse.")
        {
        }
    }
}
