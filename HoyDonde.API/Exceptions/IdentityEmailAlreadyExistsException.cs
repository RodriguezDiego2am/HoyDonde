using System;

namespace HoyDonde.API.Exceptions
{
    public class IdentityEmailAlreadyExistsException : Exception
    {
        public IdentityEmailAlreadyExistsException(string email)
            : base($"Ya existe una identidad con el email '{email}'.")
        {
        }
    }
}
