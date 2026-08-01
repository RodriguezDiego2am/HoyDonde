using System;

namespace HoyDonde.API.Exceptions
{
    public class RolYaExisteException : Exception
    {
        public RolYaExisteException(string codigo)
            : base($"Ya existe un rol con el código '{codigo}'.")
        {
        }
    }
}
