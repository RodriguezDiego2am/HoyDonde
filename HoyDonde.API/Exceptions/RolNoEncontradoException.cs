using System;

namespace HoyDonde.API.Exceptions
{
    public class RolNoEncontradoException : Exception
    {
        public RolNoEncontradoException(string codigo)
            : base($"No existe un rol con el código '{codigo}'.")
        {
        }
    }
}
