using System;

namespace HoyDonde.API.Exceptions
{
    // Combinación de filtros inválida en el reporte de eventos (docs/api-mvp-plan.md §11): hoy,
    // exclusivamente "ticketTypeId sin eventId". Se mapea a HTTP 400 con code estable
    // REPORT_FILTER_INVALID.
    public class ReporteFiltroInvalidoException : Exception
    {
        public ReporteFiltroInvalidoException(string message)
            : base(message)
        {
        }
    }
}
