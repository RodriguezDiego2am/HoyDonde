using System;

namespace HoyDonde.API.Exceptions
{
    // La PersonaId recibida en POST /api/events/{eventId}/controls/{controlPersonaId} no
    // corresponde a un Control elegible: no existe ningún Usuario con esa PersonaId, ese
    // Usuario está inactivo, o no tiene el rol CONTROL activo (docs/api-mvp-plan.md §4). Nunca
    // se distingue cuál de los tres casos aplica en el mensaje público, para no filtrar
    // información sobre la existencia de una Persona/Usuario. Se mapea a HTTP 404.
    public class ControlInvalidoException : Exception
    {
        public ControlInvalidoException(string controlPersonaId)
            : base($"La persona '{controlPersonaId}' no corresponde a un Control elegible.")
        {
        }
    }
}
