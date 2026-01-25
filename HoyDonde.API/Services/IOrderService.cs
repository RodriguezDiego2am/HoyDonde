using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IOrderService
    {
        Task<Order> CreateOrderAsync(CreateOrderRequest request);
        Task<Order?> GetOrderByIdAsync(int id);
        Task<Order?> GetOrderByNumberAsync(string orderNumber);
        Task<IEnumerable<Order>> GetOrdersByClienteIdAsync(string clienteId);
        Task<Order> CompleteOrderAsync(int orderId);
        Task CancelOrderAsync(int orderId);
        Task ExpireOldOrdersAsync();
        Task<OrderSummary> GetOrderSummaryAsync(int orderId);
    }

    public class CreateOrderRequest
    {
        public string ClienteId { get; set; } = string.Empty;
        public List<OrderItemRequest> Items { get; set; } = new();
        public int? ReservationId { get; set; }
        public string? CouponCode { get; set; }
    }

    public class OrderSummary
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public OrderStatus Estado { get; set; }
        public DateTime FechaCreacion { get; set; }
        public decimal SubTotal { get; set; }
        public decimal Fees { get; set; }
        public decimal Taxes { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal Total { get; set; }
        public List<OrderItemSummary> Items { get; set; } = new();
    }

    public class OrderItemSummary
    {
        public int TicketTypeId { get; set; }
        public string TicketTypeName { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Subtotal { get; set; }
    }
}
