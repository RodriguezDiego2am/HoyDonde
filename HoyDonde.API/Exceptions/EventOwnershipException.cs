using System;

namespace HoyDonde.API.Exceptions
{
    public class EventOwnershipException : Exception
    {
        public EventOwnershipException(string eventId, string actorId)
            : base($"El usuario '{actorId}' no es propietario del evento '{eventId}'.")
        {
        }
    }
}
