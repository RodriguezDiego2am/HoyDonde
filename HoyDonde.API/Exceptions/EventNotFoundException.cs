using System;

namespace HoyDonde.API.Exceptions
{
    public class EventNotFoundException : Exception
    {
        public EventNotFoundException(string eventId)
            : base($"El evento '{eventId}' no existe.")
        {
        }
    }
}
