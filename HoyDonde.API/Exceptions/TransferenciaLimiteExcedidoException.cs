using System;

namespace HoyDonde.API.Exceptions
{
    // The sender already completed the maximum number of transfers in the rolling window. HTTP 429.
    public class TransferenciaLimiteExcedidoException : Exception
    {
        public TransferenciaLimiteExcedidoException()
            : base("Alcanzaste el límite de transferencias por hora. Intentá de nuevo más tarde.")
        {
        }
    }
}
