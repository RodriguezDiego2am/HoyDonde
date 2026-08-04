using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Opciones reales para poblar los selectores de evento/tipo de entrada del reporte de ventas
    // (docs/api-mvp-plan.md §11.11): calculadas en memoria sobre el mismo conjunto de Compras ya
    // consultado -nunca una query Firestore adicional-, para que el filtro de evento no dependa de
    // que ese evento aparezca en el Top 5 (máximo 5 filas).
    //
    // Eventos: calculados después de rango/ownership/organizador/categoría, pero ANTES de aplicar
    // eventId -así siguen presentes aunque ya haya un eventId seleccionado, para poder cambiarlo
    // sin limpiar todo el filtro-. Únicos por EventoId, ordenados por Nombre y luego Id.
    //
    // TiposEntrada: solo se calculan cuando el filtro trae eventId (Organizador siempre que
    // corresponda; el reporte del Administrador no tiene ticketTypeId en su contrato, así que acá
    // siempre queda vacío). Únicos por TicketTypeId, ordenados por Nombre y luego Id, calculados
    // antes de aplicar ticketTypeId.
    public class VentasFiltrosDisponiblesDto
    {
        public List<VentasEventoOpcionDto> Eventos { get; set; } = new();
        public List<VentasTicketTypeOpcionDto> TiposEntrada { get; set; } = new();
    }
}
