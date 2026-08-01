using System;

namespace HoyDonde.API.Exceptions
{
    public class AccionNoEncontradaException : Exception
    {
        public AccionNoEncontradaException(string codigo)
            : base($"No existe una acción con el código '{codigo}'.")
        {
        }
    }
}
