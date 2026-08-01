using System;

namespace HoyDonde.API.Exceptions
{
    // El token es válido (autenticación de Firebase superada) pero no existe ningún Usuario
    // provisionado para ese UID en el modelo nuevo (docs/security-refactor-plan.md §1/§4).
    // El mensaje público (Message) es deliberadamente genérico y no incluye el UID: los
    // controllers y el middleware lo devuelven tal cual en la respuesta HTTP. ExternalSubjectId
    // se conserva como propiedad separada solo para logging interno estructurado.
    public class IdentityNotProvisionedException : Exception
    {
        public string ExternalSubjectId { get; }

        public IdentityNotProvisionedException(string externalSubjectId)
            : base("No existe una identidad aprovisionada para el actor autenticado.")
        {
            ExternalSubjectId = externalSubjectId;
        }
    }
}
