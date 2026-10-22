using System.ComponentModel.DataAnnotations;

namespace HoyDonde.API.DTOs
{
    public sealed class PurchaseStartRequest
    {
        [Required] public string EventoId { get; init; } = string.Empty;
        [Required] public string TicketTypeId { get; init; } = string.Empty;
        [Range(1, 10)] public int Cantidad { get; init; }
    }

    public sealed class PurchaseStatusResponse
    {
        public string Id { get; init; } = string.Empty;
        public string EstadoCompra { get; init; } = string.Empty;
        public string EstadoReserva { get; init; } = string.Empty;
        public string CheckoutState { get; init; } = string.Empty;
        public string? CheckoutUrl { get; init; }
        public string? RefundStatus { get; init; }
    }
}
