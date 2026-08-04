using System;

namespace HoyDonde.API.Exceptions
{
    // El Usuario existe pero no tiene una identidad de proveedor externo recuperable (falta
    // IdentityProvider/ExternalSubjectId, o no es Firebase) -no debería ocurrir en el modelo
    // actual (docs/security-refactor-plan.md §2: toda provisión escribe ambos campos), pero el
    // endpoint de recuperación de contraseña (docs/api-mvp-plan.md §13) lo trata como un estado
    // de datos posible en vez de asumirlo imposible.
    public class UsuarioSinIdentidadRecuperableException : Exception
    {
        public UsuarioSinIdentidadRecuperableException(string usuarioId)
            : base($"El usuario '{usuarioId}' no tiene una identidad de Firebase recuperable.")
        {
        }
    }
}
