using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IPaymentService
    {
        Task<Payment> CreatePaymentAsync(int orderId, decimal monto, PaymentMethod metodo, string? paymentMethodToken = null);
        Task<Payment> ProcessPaymentAsync(int paymentId);
        Task<Payment?> GetPaymentByIdAsync(int id);
        Task<Payment?> GetPaymentByTransactionIdAsync(string transactionId);
        Task<bool> RefundPaymentAsync(int paymentId, decimal? amount = null);
        Task HandlePaymentWebhookAsync(string gateway, string payload, string signature);
    }
}
