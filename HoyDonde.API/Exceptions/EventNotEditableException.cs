using System;

namespace HoyDonde.API.Exceptions
{
    // UpdateEventAsync solo es válido mientras el evento está en Borrador (docs/api-mvp-plan.md
    // §2). Se mapea a HTTP 409.
    public class EventNotEditableException : Exception
    {
        public EventNotEditableException(string eventId)
            : base($"El evento '{eventId}' no puede editarse en su estado actual. Solo un evento en Borrador es editable.")
        {
        }
    }
}
