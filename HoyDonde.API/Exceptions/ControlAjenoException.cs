using System;

namespace HoyDonde.API.Exceptions
{
    // El Control existe y es elegible, pero nunca tuvo una asignación creada por este
    // organizador (docs/api-mvp-plan.md §4): un organizador no puede apropiarse de un Control
    // que solo trabajó hasta ahora para otro organizador. Se mapea a HTTP 403.
    public class ControlAjenoException : Exception
    {
        public ControlAjenoException(string controlPersonaId)
            : base($"La persona '{controlPersonaId}' no pertenece al ámbito de asignaciones de este organizador.")
        {
        }
    }
}
