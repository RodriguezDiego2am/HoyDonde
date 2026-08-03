using System;

namespace HoyDonde.API.Exceptions
{
    // Baja física de roles (docs/api-mvp-plan.md §12): solo se permite eliminar físicamente un
    // rol que ya fue dado de baja lógica (Activo == false). Un rol activo debe desactivarse
    // primero.
    public class RolDebeEstarInactivoException : Exception
    {
        public RolDebeEstarInactivoException(string codigo)
            : base($"El rol '{codigo}' debe estar inactivo antes de poder eliminarse físicamente.")
        {
        }
    }
}
