using System;

namespace HoyDonde.API.Exceptions
{
    // Validación de datos de Event/TicketGroup a nivel de servicio (defensa en profundidad detrás
    // de las DataAnnotations de los DTOs, docs/api-mvp-plan.md §2). Se mapea a HTTP 400.
    public class EventValidationException : Exception
    {
        public EventValidationException(string message)
            : base(message)
        {
        }
    }
}
