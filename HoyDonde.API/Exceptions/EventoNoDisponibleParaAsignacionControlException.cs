using System;

namespace HoyDonde.API.Exceptions
{
    // El evento destino está Cancelado, o Publicado con estado efectivo Finalizado
    // (docs/api-mvp-plan.md §4): ninguno admite nuevas asignaciones de Control. Se mapea a
    // HTTP 409.
    public class EventoNoDisponibleParaAsignacionControlException : Exception
    {
        public EventoNoDisponibleParaAsignacionControlException(string eventId)
            : base($"El evento '{eventId}' no admite asignación de Control en este momento.")
        {
        }
    }
}
