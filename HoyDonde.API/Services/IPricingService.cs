using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IPricingService
    {
        Task<PricingResult> CalculatePricingAsync(List<OrderItemRequest> items, string? couponCode = null);
        Task<Fee?> GetFeeByIdAsync(int id);
        Task<IEnumerable<Fee>> GetActiveFeesAsync();
    }

    public class OrderItemRequest
    {
        public int TicketTypeId { get; set; }
        public int Cantidad { get; set; }
    }

    public class PricingResult
    {
        public decimal SubTotal { get; set; }
        public decimal Fees { get; set; }
        public decimal Taxes { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal Total { get; set; }
        public List<PricingBreakdown> Breakdown { get; set; } = new();
    }

    public class PricingBreakdown
    {
        public string Concepto { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Tipo { get; set; } = string.Empty; // "subtotal", "fee", "tax", "discount"
    }
}
