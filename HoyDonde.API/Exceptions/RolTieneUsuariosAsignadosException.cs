using System;

namespace HoyDonde.API.Exceptions
{
    // Baja física de roles (docs/api-mvp-plan.md §12): un rol con al menos una asignación
    // UsuarioRol -activa o inactiva- nunca puede eliminarse físicamente, para no dejar
    // referencias huérfanas (usuarios/{id}/roles/{codigo} apuntando a un Rol borrado).
    public class RolTieneUsuariosAsignadosException : Exception
    {
        public RolTieneUsuariosAsignadosException(string codigo)
            : base($"El rol '{codigo}' tiene usuarios asignados (activos o inactivos) y no puede eliminarse físicamente.")
        {
        }
    }
}
