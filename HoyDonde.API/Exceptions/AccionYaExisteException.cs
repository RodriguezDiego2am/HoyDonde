using System;

namespace HoyDonde.API.Exceptions
{
    public class AccionYaExisteException : Exception
    {
        public AccionYaExisteException(string codigo)
            : base($"Ya existe una acción con el código '{codigo}'.")
        {
        }
    }
}
