using System;

namespace HoyDonde.API.Exceptions
{
    // The recipient email does not resolve to an active, ticket-capable account. Unknown email,
    // inactive account and account without ticket access collapse into this single public
    // message, which never echoes the email back. HTTP 404.
    public class TransferenciaDestinatarioInvalidoException : Exception
    {
        public TransferenciaDestinatarioInvalidoException()
            : base("No pudimos transferir la entrada a ese destinatario. Verificá que tenga una cuenta activa en HoyDonde.")
        {
        }
    }
}
