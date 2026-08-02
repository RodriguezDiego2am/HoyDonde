namespace HoyDonde.API.DTOs
{
    // DTO de salida para un tipo de ticket ya persistido (a diferencia de TicketGroupDto, que es
    // exclusivamente de entrada para crear/editar un evento). Expone el TicketTypeId real
    // generado por el servidor, para que un cliente pueda resolver qué tipo de ticket comprar
    // (TicketBuyRequest.TicketTypeId) usando exclusivamente la respuesta de EventResponse — nunca
    // se acepta ni se necesita este Id en el request de creación/edición de un evento.
    public class TicketTypeResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public int CantidadDisponible { get; set; }
    }
}
