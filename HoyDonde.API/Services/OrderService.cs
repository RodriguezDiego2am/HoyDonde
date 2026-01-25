using HoyDonde.API.Data;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HoyDonde.API.Services
{
    public class OrderService : IOrderService
    {
        private readonly ApplicationDbContext _context;
        private readonly IOrderRepository _orderRepository;
        private readonly IReservationService _reservationService;
        private readonly IPricingService _pricingService;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            ApplicationDbContext context,
            IOrderRepository orderRepository,
            IReservationService reservationService,
            IPricingService pricingService,
            ILogger<OrderService> logger)
        {
            _context = context;
            _orderRepository = orderRepository;
            _reservationService = reservationService;
            _pricingService = pricingService;
            _logger = logger;
        }

        public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
        {
            // 1. Validar si hay una reserva asociada
            if (request.ReservationId.HasValue)
            {
                var isValid = await _reservationService.ValidateReservationAsync(request.ReservationId.Value);
                if (!isValid)
                    throw new InvalidOperationException("La reserva no es válida o ha expirado");
            }
            else
            {
                // Si no hay reserva, verificar stock disponible
                foreach (var item in request.Items)
                {
                    if (!await _reservationService.HasAvailableStockAsync(item.TicketTypeId, item.Cantidad))
                    {
                        var ticketType = await _context.TicketTypes.FindAsync(item.TicketTypeId);
                        throw new InvalidOperationException($"No hay suficiente stock disponible para {ticketType?.Nombre ?? "el tipo de entrada"}");
                    }
                }
            }

            // 2. Calcular precios
            var pricing = await _pricingService.CalculatePricingAsync(request.Items, request.CouponCode);

            // 3. Crear la orden
            var order = new Order
            {
                OrderNumber = GenerateOrderNumber(),
                ClienteId = request.ClienteId,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow,
                Estado = OrderStatus.Pending,
                SubTotal = pricing.SubTotal,
                Fees = pricing.Fees,
                Taxes = pricing.Taxes,
                DiscountAmount = pricing.DiscountAmount,
                Total = pricing.Total,
                CouponCode = request.CouponCode,
                ReservationId = request.ReservationId
            };

            // 4. Agregar items a la orden
            foreach (var itemRequest in request.Items)
            {
                var ticketType = await _context.TicketTypes.FindAsync(itemRequest.TicketTypeId);
                if (ticketType == null)
                    throw new ArgumentException($"TicketType con ID {itemRequest.TicketTypeId} no encontrado");

                var orderItem = new OrderItem
                {
                    TicketTypeId = itemRequest.TicketTypeId,
                    Cantidad = itemRequest.Cantidad,
                    PrecioUnitario = ticketType.Precio,
                    Subtotal = ticketType.Precio * itemRequest.Cantidad
                };

                order.Items.Add(orderItem);
            }

            // 5. Guardar la orden
            await _orderRepository.AddAsync(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Orden {order.OrderNumber} creada exitosamente para cliente {request.ClienteId}");

            return await _orderRepository.GetByIdWithDetailsAsync(order.Id)
                ?? throw new InvalidOperationException("Error al crear la orden");
        }

        public async Task<Order?> GetOrderByIdAsync(int id)
        {
            return await _orderRepository.GetByIdWithDetailsAsync(id);
        }

        public async Task<Order?> GetOrderByNumberAsync(string orderNumber)
        {
            return await _orderRepository.GetByOrderNumberAsync(orderNumber);
        }

        public async Task<IEnumerable<Order>> GetOrdersByClienteIdAsync(string clienteId)
        {
            return await _orderRepository.GetByClienteIdAsync(clienteId);
        }

        public async Task<Order> CompleteOrderAsync(int orderId)
        {
            var order = await _orderRepository.GetByIdWithDetailsAsync(orderId);
            if (order == null)
                throw new ArgumentException($"Orden con ID {orderId} no encontrada");

            if (order.Estado != OrderStatus.Processing)
                throw new InvalidOperationException($"La orden debe estar en estado Processing para completarse. Estado actual: {order.Estado}");

            // 1. Marcar reserva como completada si existe
            if (order.ReservationId.HasValue)
            {
                await _reservationService.CompleteReservationAsync(order.ReservationId.Value, orderId);
            }

            // 2. Generar tickets para cada item
            foreach (var item in order.Items)
            {
                for (int i = 0; i < item.Cantidad; i++)
                {
                    var ticket = new Ticket
                    {
                        TicketTypeId = item.TicketTypeId,
                        ClienteId = order.ClienteId,
                        OrderItemId = item.Id,
                        FechaCompra = DateTime.UtcNow,
                        QrCode = GenerateUniqueQrCode(),
                        QrCodeHash = string.Empty, // TODO: Generar hash
                        Estado = TicketStatus.Valid,
                        EsUsado = false,
                        EsCortesia = false,
                        PrecioFinal = item.PrecioUnitario
                    };

                    item.Tickets.Add(ticket);
                }
            }

            // 3. Actualizar estado de la orden
            order.Estado = OrderStatus.Paid;
            order.FechaActualizacion = DateTime.UtcNow;

            _orderRepository.Update(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Orden {order.OrderNumber} completada exitosamente. {order.Items.Sum(i => i.Cantidad)} tickets generados");

            return await _orderRepository.GetByIdWithDetailsAsync(orderId)
                ?? throw new InvalidOperationException("Error al completar la orden");
        }

        public async Task CancelOrderAsync(int orderId)
        {
            var order = await _orderRepository.GetByIdWithDetailsAsync(orderId);
            if (order == null)
                throw new ArgumentException($"Orden con ID {orderId} no encontrada");

            if (order.Estado == OrderStatus.Paid || order.Estado == OrderStatus.Completed)
                throw new InvalidOperationException("No se puede cancelar una orden que ya fue pagada. Use el proceso de reembolso");

            // Cancelar reserva si existe
            if (order.ReservationId.HasValue)
            {
                await _reservationService.CancelReservationAsync(order.ReservationId.Value);
            }

            order.Estado = OrderStatus.Cancelled;
            order.FechaActualizacion = DateTime.UtcNow;

            _orderRepository.Update(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Orden {order.OrderNumber} cancelada");
        }

        public async Task ExpireOldOrdersAsync()
        {
            // Expirar órdenes pendientes por más de 15 minutos
            var expirationTime = DateTime.UtcNow.AddMinutes(-15);
            var expiredOrders = await _orderRepository.GetExpiredOrdersAsync(expirationTime);

            foreach (var order in expiredOrders)
            {
                // Cancelar reserva si existe
                if (order.ReservationId.HasValue && order.Reservation != null)
                {
                    order.Reservation.Estado = ReservationStatus.Expired;
                }

                order.Estado = OrderStatus.Expired;
                order.FechaActualizacion = DateTime.UtcNow;

                _orderRepository.Update(order);
            }

            if (expiredOrders.Any())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation($"{expiredOrders.Count()} órdenes expiradas");
            }
        }

        public async Task<OrderSummary> GetOrderSummaryAsync(int orderId)
        {
            var order = await _orderRepository.GetByIdWithDetailsAsync(orderId);
            if (order == null)
                throw new ArgumentException($"Orden con ID {orderId} no encontrada");

            return new OrderSummary
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Estado = order.Estado,
                FechaCreacion = order.FechaCreacion,
                SubTotal = order.SubTotal,
                Fees = order.Fees,
                Taxes = order.Taxes,
                DiscountAmount = order.DiscountAmount,
                Total = order.Total,
                Items = order.Items.Select(i => new OrderItemSummary
                {
                    TicketTypeId = i.TicketTypeId,
                    TicketTypeName = i.TicketType.Nombre,
                    Cantidad = i.Cantidad,
                    PrecioUnitario = i.PrecioUnitario,
                    Subtotal = i.Subtotal
                }).ToList()
            };
        }

        private string GenerateOrderNumber()
        {
            // Formato: ORD-YYYYMMDD-XXXXX
            var date = DateTime.UtcNow.ToString("yyyyMMdd");
            var random = new Random().Next(10000, 99999);
            return $"ORD-{date}-{random}";
        }

        private string GenerateUniqueQrCode()
        {
            // Generar un código QR único
            // Formato: TICKET-GUID
            return $"TICKET-{Guid.NewGuid().ToString("N").ToUpper()}";
        }
    }
}
