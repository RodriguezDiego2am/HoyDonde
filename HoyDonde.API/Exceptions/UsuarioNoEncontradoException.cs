using System;

namespace HoyDonde.API.Exceptions
{
    public class UsuarioNoEncontradoException : Exception
    {
        public UsuarioNoEncontradoException(string usuarioId)
            : base($"No existe un usuario con el id '{usuarioId}'.")
        {
        }
    }
}
