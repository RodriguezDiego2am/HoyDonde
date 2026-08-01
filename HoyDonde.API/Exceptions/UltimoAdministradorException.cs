using System;

namespace HoyDonde.API.Exceptions
{
    // Guard transaccional de "no dejar cero Administradores efectivos"
    // (docs/security-refactor-plan.md §6, Etapa 5). Un Administrador efectivo es un Usuario
    // activo con una asignación UsuarioRol(ADMINISTRADOR) activa sobre un Rol ADMINISTRADOR
    // también activo. Se lanza dentro de la transacción Firestore que evalúa la operación, antes
    // de escribir nada: la transacción completa se descarta.
    public class UltimoAdministradorException : Exception
    {
        public UltimoAdministradorException()
            : base("La operación dejaría el sistema sin ningún Administrador efectivo.")
        {
        }
    }
}
