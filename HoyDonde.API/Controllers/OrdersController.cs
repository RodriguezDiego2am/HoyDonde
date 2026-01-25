using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HoyDonde.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly IReservationService _reservationService;
        private readonly IPaymentService _paymentService;
        private readonly IPricingService _pricingService;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(
            IOrderService orderService,
            IReservationService reservationService,
            IPaymentService paymentService,
            IPricingService pricingService,
            ILogger<OrdersController> logger)
        {
            _orderService = orderService;
            _reservationService = reservationService;
            _paymentService = paymentService;
            _pricingService = pricingService;
            _logger = logger;
        }

        // POST: api/orders/calculate-price
        [HttpPost("calculate-price")]
        [AllowAnonymous]
        public async Task<ActionResult<PricingResult>> CalculatePrice([FromBody] CalculatePriceRequest request)
        {
            try
            {
                var pricing = await _pricingService.CalculatePricingAsync(request.Items, request.CouponCode);
                return Ok(pricing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular precio");
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/orders/reserve
        [HttpPost("reserve")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<Reservation>> CreateReservation([FromBody] CreateReservationRequest request)
        {
            try
            {
                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(clienteId))
                    return Unauthorized();

                var reservation = await _reservationService.CreateReservationAsync(
                    clienteId,
                    request.Items,
                    request.DurationMinutes ?? 10
                );

                return Ok(reservation);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear reserva");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // GET: api/orders/reservation/{id}
        [HttpGet("reservation/{id}")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<Reservation>> GetReservation(int id)
        {
            try
            {
                var reservation = await _reservationService.GetReservationByIdAsync(id);
                if (reservation == null)
                    return NotFound();

                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (reservation.ClienteId != clienteId)
                    return Forbid();

                return Ok(reservation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener reserva {id}");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // DELETE: api/orders/reservation/{id}
        [HttpDelete("reservation/{id}")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult> CancelReservation(int id)
        {
            try
            {
                var reservation = await _reservationService.GetReservationByIdAsync(id);
                if (reservation == null)
                    return NotFound();

                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (reservation.ClienteId != clienteId)
                    return Forbid();

                await _reservationService.CancelReservationAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al cancelar reserva {id}");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // POST: api/orders
        [HttpPost]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<Order>> CreateOrder([FromBody] CreateOrderRequest request)
        {
            try
            {
                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(clienteId))
                    return Unauthorized();

                request.ClienteId = clienteId;
                var order = await _orderService.CreateOrderAsync(request);

                return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear orden");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // GET: api/orders/{id}
        [HttpGet("{id}")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(id);
                if (order == null)
                    return NotFound();

                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (order.ClienteId != clienteId)
                    return Forbid();

                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener orden {id}");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // GET: api/orders/number/{orderNumber}
        [HttpGet("number/{orderNumber}")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<Order>> GetOrderByNumber(string orderNumber)
        {
            try
            {
                var order = await _orderService.GetOrderByNumberAsync(orderNumber);
                if (order == null)
                    return NotFound();

                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (order.ClienteId != clienteId)
                    return Forbid();

                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener orden {orderNumber}");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // GET: api/orders/my-orders
        [HttpGet("my-orders")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<IEnumerable<Order>>> GetMyOrders()
        {
            try
            {
                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(clienteId))
                    return Unauthorized();

                var orders = await _orderService.GetOrdersByClienteIdAsync(clienteId);
                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener órdenes del cliente");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // GET: api/orders/{id}/summary
        [HttpGet("{id}/summary")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<OrderSummary>> GetOrderSummary(int id)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(id);
                if (order == null)
                    return NotFound();

                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (order.ClienteId != clienteId)
                    return Forbid();

                var summary = await _orderService.GetOrderSummaryAsync(id);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener resumen de orden {id}");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // POST: api/orders/{id}/cancel
        [HttpPost("{id}/cancel")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult> CancelOrder(int id)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(id);
                if (order == null)
                    return NotFound();

                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (order.ClienteId != clienteId)
                    return Forbid();

                await _orderService.CancelOrderAsync(id);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al cancelar orden {id}");
                return StatusCode(500, new { message = "Error interno del servidor" });
            }
        }

        // POST: api/orders/{id}/pay
        [HttpPost("{id}/pay")]
        [Authorize(Roles = Roles.Cliente)]
        public async Task<ActionResult<Payment>> ProcessPayment(int id, [FromBody] ProcessPaymentRequest request)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(id);
                if (order == null)
                    return NotFound();

                var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (order.ClienteId != clienteId)
                    return Forbid();

                // Crear el pago
                var payment = await _paymentService.CreatePaymentAsync(
                    id,
                    order.Total,
                    request.MetodoPago,
                    request.PaymentMethodToken
                );

                // Procesar el pago
                payment = await _paymentService.ProcessPaymentAsync(payment.Id);

                // Si el pago fue exitoso, completar la orden
                if (payment.Estado == PaymentStatus.Approved || payment.Estado == PaymentStatus.Completed)
                {
                    await _orderService.CompleteOrderAsync(id);
                }

                return Ok(payment);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar pago para orden {id}");
                return StatusCode(500, new { message = "Error al procesar el pago" });
            }
        }

        // POST: api/orders/webhook/{gateway}
        [HttpPost("webhook/{gateway}")]
        [AllowAnonymous]
        public async Task<ActionResult> HandlePaymentWebhook(string gateway, [FromBody] object payload)
        {
            try
            {
                var signature = Request.Headers["X-Signature"].FirstOrDefault() ?? string.Empty;
                var payloadString = System.Text.Json.JsonSerializer.Serialize(payload);

                await _paymentService.HandlePaymentWebhookAsync(gateway, payloadString, signature);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al procesar webhook de {gateway}");
                return StatusCode(500);
            }
        }
    }

    // DTOs
    public class CalculatePriceRequest
    {
        public List<OrderItemRequest> Items { get; set; } = new();
        public string? CouponCode { get; set; }
    }

    public class CreateReservationRequest
    {
        public List<ReservationItemRequest> Items { get; set; } = new();
        public int? DurationMinutes { get; set; } = 10;
    }

    public class ProcessPaymentRequest
    {
        public PaymentMethod MetodoPago { get; set; }
        public string? PaymentMethodToken { get; set; }
    }
}
