using System;

namespace HoyDonde.API.Exceptions
{
    // The ticket does not exist or is not owned by the caller. Both cases are deliberately
    // indistinguishable so the endpoint cannot be used to probe other people's ticket ids. HTTP 404.
    public class TicketNoEncontradoException : Exception
    {
        public TicketNoEncontradoException()
            : base("Entrada no encontrada.")
        {
        }
    }
}
