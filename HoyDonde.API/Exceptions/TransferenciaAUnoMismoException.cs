using System;

namespace HoyDonde.API.Exceptions
{
    // The recipient resolves to the caller's own Persona. The caller already knows their own
    // email, so a distinct message leaks nothing. HTTP 400.
    public class TransferenciaAUnoMismoException : Exception
    {
        public TransferenciaAUnoMismoException()
            : base("No podés transferirte una entrada a vos mismo.")
        {
        }
    }
}
