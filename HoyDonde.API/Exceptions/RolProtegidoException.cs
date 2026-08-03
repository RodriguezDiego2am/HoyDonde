using System;

namespace HoyDonde.API.Exceptions
{
    // Baja física de roles (docs/api-mvp-plan.md §12): los 4 roles esenciales sembrados
    // (ADMINISTRADOR/ORGANIZADOR/CLIENTE/CONTROL) nunca pueden eliminarse físicamente, activos o
    // inactivos, tengan o no usuarios asignados.
    public class RolProtegidoException : Exception
    {
        public RolProtegidoException(string codigo)
            : base($"El rol '{codigo}' es un rol esencial y no puede eliminarse físicamente.")
        {
        }
    }
}
