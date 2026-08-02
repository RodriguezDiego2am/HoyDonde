using System;

namespace HoyDonde.API.Exceptions
{
    // Transición inválida en la máquina de estados de Event (docs/api-mvp-plan.md §1), incluida la
    // cancelación de un evento cuyo estado efectivo ya es Finalizado. Se mapea a HTTP 409.
    public class EventInvalidTransitionException : Exception
    {
        public EventInvalidTransitionException(string eventId, string estadoActual, string transicionIntentada)
            : base($"El evento '{eventId}' no puede transicionar de '{estadoActual}' a '{transicionIntentada}'.")
        {
        }
    }
}
