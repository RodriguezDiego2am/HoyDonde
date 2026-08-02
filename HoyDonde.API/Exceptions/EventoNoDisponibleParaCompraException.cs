using System;

namespace HoyDonde.API.Exceptions
{
    // El evento no cumple la vigencia de compra (docs/api-mvp-plan.md §0.1/§3):
    // Estado == Publicado && UtcNow < FechaInicio. Se mapea a HTTP 409.
    public class EventoNoDisponibleParaCompraException : Exception
    {
        public EventoNoDisponibleParaCompraException(string eventId)
            : base($"El evento '{eventId}' no admite compra de tickets en este momento.")
        {
        }
    }
}
