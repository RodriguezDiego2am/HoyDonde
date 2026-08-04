namespace HoyDonde.API.DTOs
{
    // Desglose por tipo de entrada del reporte de ventas (docs/api-mvp-plan.md §11): solo se
    // calcula cuando el filtro trae un eventId (Organizador: siempre ownership-verificado antes;
    // Administrador: filtro explícito), porque TicketTypeId solo es comparable dentro de un mismo
    // evento. CantidadComprasDistintas cuenta Compra.Id distintos entre los Ticket de ese tipo
    // (hoy siempre coincide con la cantidad de Compras, porque cada Compra actual es homogénea a
    // un único TicketTypeId, pero se calcula por conteo real para no asumirlo).
    public class VentasTicketTypeDto
    {
        public string TicketTypeId { get; set; } = string.Empty;
        public string TicketTypeNombre { get; set; } = string.Empty;
        public int CantidadComprasDistintas { get; set; }
        public int EntradasEmitidas { get; set; }
        public decimal ImporteEmitido { get; set; }
        public double PorcentajeDelImporteTotal { get; set; }
    }
}
