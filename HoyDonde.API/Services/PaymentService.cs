using HoyDonde.API.Data;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using System.Text.Json;

namespace HoyDonde.API.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            ApplicationDbContext context,
            IPaymentRepository paymentRepository,
            IOrderRepository orderRepository,
            ILogger<PaymentService> logger)
        {
            _context = context;
            _paymentRepository = paymentRepository;
            _orderRepository = orderRepository;
            _logger = logger;
        }

        public async Task<Payment> CreatePaymentAsync(int orderId, decimal monto, PaymentMethod metodo, string? paymentMethodToken = null)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new ArgumentException($"Orden con ID {orderId} no encontrada");

            if (order.Estado != OrderStatus.Pending)
                throw new InvalidOperationException($"La orden debe estar en estado Pending para crear un pago. Estado actual: {order.Estado}");

            var payment = new Payment
            {
                OrderId = orderId,
                Monto = monto,
                MetodoPago = metodo,
                Estado = PaymentStatus.Pending,
                FechaCreacion = DateTime.UtcNow,
                PaymentMethodToken = paymentMethodToken
            };

            await _paymentRepository.AddAsync(payment);
            await _context.SaveChangesAsync();

            return payment;
        }

        public async Task<Payment> ProcessPaymentAsync(int paymentId)
        {
            var payment = await _paymentRepository.GetByIdWithDetailsAsync(paymentId);
            if (payment == null)
                throw new ArgumentException($"Pago con ID {paymentId} no encontrado");

            if (payment.Estado != PaymentStatus.Pending)
                throw new InvalidOperationException($"El pago ya fue procesado. Estado actual: {payment.Estado}");

            try
            {
                payment.Estado = PaymentStatus.Processing;
                _paymentRepository.Update(payment);
                await _context.SaveChangesAsync();

                // TODO: Integrar con pasarela de pago real (MercadoPago, Stripe, etc.)
                // Por ahora simulamos un pago exitoso
                await SimulatePaymentGatewayAsync(payment);

                payment.Estado = PaymentStatus.Approved;
                payment.FechaProcesado = DateTime.UtcNow;

                // Actualizar estado de la orden
                var order = payment.Order;
                order.Estado = OrderStatus.Processing;
                order.FechaActualizacion = DateTime.UtcNow;

                _paymentRepository.Update(payment);
                _orderRepository.Update(order);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Pago {paymentId} procesado exitosamente para la orden {payment.OrderId}");

                return payment;
            }
            catch (Exception ex)
            {
                payment.Estado = PaymentStatus.Failed;
                payment.ErrorMessage = ex.Message;
                _paymentRepository.Update(payment);
                await _context.SaveChangesAsync();

                _logger.LogError(ex, $"Error al procesar pago {paymentId}");
                throw;
            }
        }

        public async Task<Payment?> GetPaymentByIdAsync(int id)
        {
            return await _paymentRepository.GetByIdWithDetailsAsync(id);
        }

        public async Task<Payment?> GetPaymentByTransactionIdAsync(string transactionId)
        {
            return await _paymentRepository.GetByTransactionIdAsync(transactionId);
        }

        public async Task<bool> RefundPaymentAsync(int paymentId, decimal? amount = null)
        {
            var payment = await _paymentRepository.GetByIdWithDetailsAsync(paymentId);
            if (payment == null)
                throw new ArgumentException($"Pago con ID {paymentId} no encontrado");

            if (payment.Estado != PaymentStatus.Completed && payment.Estado != PaymentStatus.Approved)
                throw new InvalidOperationException("Solo se pueden reembolsar pagos completados o aprobados");

            try
            {
                // TODO: Integrar con pasarela de pago para reembolso real
                // Por ahora solo cambiamos el estado

                payment.Estado = PaymentStatus.Refunded;
                payment.FechaProcesado = DateTime.UtcNow;

                // Actualizar orden
                var order = payment.Order;
                order.Estado = OrderStatus.Refunded;
                order.FechaActualizacion = DateTime.UtcNow;

                _paymentRepository.Update(payment);
                _orderRepository.Update(order);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Pago {paymentId} reembolsado exitosamente");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al reembolsar pago {paymentId}");
                return false;
            }
        }

        public async Task HandlePaymentWebhookAsync(string gateway, string payload, string signature)
        {
            // TODO: Implementar validación de firma HMAC según la pasarela
            // TODO: Procesar el webhook según la pasarela (MercadoPago, Stripe, etc.)

            _logger.LogInformation($"Webhook recibido de {gateway}");

            // Estructura básica para cuando se implemente
            try
            {
                // Validar firma
                // var isValid = ValidateWebhookSignature(gateway, payload, signature);
                // if (!isValid)
                //     throw new UnauthorizedAccessException("Firma de webhook inválida");

                // Parsear payload
                // var data = JsonSerializer.Deserialize<PaymentWebhookData>(payload);

                // Buscar pago por TransactionId
                // var payment = await GetPaymentByTransactionIdAsync(data.TransactionId);

                // Actualizar estado según respuesta de la pasarela
                // ...

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar webhook de pago");
                throw;
            }
        }

        private async Task SimulatePaymentGatewayAsync(Payment payment)
        {
            // Simulación de llamada a pasarela de pago
            await Task.Delay(500); // Simular latencia de red

            // Generar un ID de transacción simulado
            payment.TransactionId = $"TXN-{Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper()}";
            payment.PaymentGateway = "Simulado";

            var gatewayResponse = new
            {
                status = "approved",
                transaction_id = payment.TransactionId,
                amount = payment.Monto,
                timestamp = DateTime.UtcNow
            };

            payment.PaymentGatewayResponse = JsonSerializer.Serialize(gatewayResponse);
        }
    }
}
