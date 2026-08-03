using System;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    // Validaciones puras del reporte de eventos (docs/api-mvp-plan.md §11): rango de fechas,
    // dependencia ticketTypeId->eventId, ownership y filtros en memoria (estado/categoría/rango).
    // Deliberadamente sin FirestoreDb: recibe siempre un Event ya leído por el llamador
    // (ReporteService), para poder testearse con objetos en memoria, sin Firestore Emulator.
    public static class ReporteFiltroValidator
    {
        private const int MaxRangoDias = 366;

        // Ambos extremos son obligatorios y deben venir en UTC explícito (nunca interpretados
        // silenciosamente con la zona horaria del servidor, docs/api-mvp-plan.md §11.6): se exige
        // DateTimeKind.Utc, no solo "tiene un valor". Desde estrictamente anterior a Hasta (Hasta
        // es exclusiva) y el rango no puede exceder MaxRangoDias.
        public static (DateTime Desde, DateTime Hasta) ValidateRango(DateTime? fechaDesde, DateTime? fechaHasta)
        {
            if (!fechaDesde.HasValue || !fechaHasta.HasValue)
                throw new ReporteRangoInvalidoException("Los parámetros 'fechaDesde' y 'fechaHasta' son obligatorios.");

            var desde = fechaDesde.Value;
            var hasta = fechaHasta.Value;

            if (desde.Kind != DateTimeKind.Utc || hasta.Kind != DateTimeKind.Utc)
                throw new ReporteRangoInvalidoException("'fechaDesde' y 'fechaHasta' deben especificarse en UTC explícito (por ejemplo, con sufijo 'Z').");

            if (desde >= hasta)
                throw new ReporteRangoInvalidoException("'fechaDesde' debe ser anterior a 'fechaHasta'.");

            if ((hasta - desde).TotalDays > MaxRangoDias)
                throw new ReporteRangoInvalidoException($"El rango entre 'fechaDesde' y 'fechaHasta' no puede exceder {MaxRangoDias} días.");

            return (desde, hasta);
        }

        public static void ValidateTicketTypeRequiresEventId(string? eventId, string? ticketTypeId)
        {
            if (!string.IsNullOrEmpty(ticketTypeId) && string.IsNullOrEmpty(eventId))
                throw new ReporteFiltroInvalidoException("'ticketTypeId' requiere 'eventId'.");
        }

        // Nunca confía en un ownership enviado por el cliente: compara el Event ya leído desde
        // Firestore contra el actorPersonaId ya resuelto vía IAuthenticatedPersonaResolver.
        public static void EnsureOwnedEvent(Event? evento, string eventId, string actorId, string actorPersonaId)
        {
            if (evento == null) throw new EventNotFoundException(eventId);
            if (evento.OrganizadorPersonaId != actorPersonaId) throw new EventOwnershipException(eventId, actorId);
        }

        public static void EnsureTicketTypeBelongsToEvent(Event evento, string eventId, string? ticketTypeId)
        {
            if (string.IsNullOrEmpty(ticketTypeId)) return;

            var pertenece = evento.TicketTypes != null && evento.TicketTypes.Exists(t => t.Id == ticketTypeId);
            if (!pertenece) throw new TicketTypeInvalidoException(eventId, ticketTypeId);
        }

        // Filtros aplicados en memoria sobre un Event ya acotado por ownership (docs/api-mvp-plan.md
        // §11.4): rango de FechaInicio, estado efectivo y categoría. Se usa tanto para el camino
        // "sin eventId" (redundante con la query Firestore, pero inofensivo) como para el camino
        // "con eventId" (donde es la única verificación de rango/filtros: un evento propio fuera de
        // rango/filtros da un reporte vacío, nunca una fuga de datos).
        public static bool CumpleFiltros(Event evento, ReporteEventosFilterDto filter, DateTime fechaDesde, DateTime fechaHasta, DateTime utcNow)
        {
            if (evento.FechaInicio < fechaDesde || evento.FechaInicio >= fechaHasta) return false;
            if (filter.Estado.HasValue && evento.GetEstadoEfectivo(utcNow) != filter.Estado.Value) return false;
            if (filter.Categoria.HasValue && evento.Categoria != filter.Categoria.Value) return false;
            return true;
        }
    }
}
