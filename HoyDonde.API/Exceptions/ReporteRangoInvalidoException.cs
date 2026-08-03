using System;

namespace HoyDonde.API.Exceptions
{
    // Rango de fechas del reporte inválido (docs/api-mvp-plan.md §11): ausente, invertido, sin
    // UTC explícito, o mayor a 366 días. Se mapea a HTTP 400 con code estable REPORT_RANGE_INVALID.
    public class ReporteRangoInvalidoException : Exception
    {
        public ReporteRangoInvalidoException(string message)
            : base(message)
        {
        }
    }
}
