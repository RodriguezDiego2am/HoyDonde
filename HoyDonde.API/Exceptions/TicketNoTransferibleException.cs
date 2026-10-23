using System;

namespace HoyDonde.API.Exceptions
{
    // The ticket cannot be transferred anymore: already used, annulled, or its event is cancelled
    // or finished (same effective-state rule as validation). HTTP 409.
    public class TicketNoTransferibleException : Exception
    {
        public TicketNoTransferibleException()
            : base("Esta entrada ya no se puede transferir.")
        {
        }
    }
}
