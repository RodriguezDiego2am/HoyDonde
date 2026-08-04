using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Procesamiento agregado del reporte de desempeño (docs/api-mvp-plan.md §11): evento con mayor
    // ocupación/asistencia/importe emitido, y el ranking Top 5 por importe emitido. Se calcula
    // sobre los mismos Event/Ticket ya leídos para el resto del reporte -nunca una consulta
    // Firestore adicional-. Todos los campos son null/vacíos únicamente cuando no hay ningún
    // evento en el conjunto filtrado.
    public class ReporteDestacadosDto
    {
        public ReporteEventoDestacadoPorcentajeDto? EventoMayorOcupacion { get; set; }
        public ReporteEventoDestacadoPorcentajeDto? EventoMayorAsistencia { get; set; }
        public ReporteEventoDestacadoImporteDto? EventoMayorImporte { get; set; }
        public List<ReporteTopEventoDto> Top5PorImporte { get; set; } = new();
    }
}
