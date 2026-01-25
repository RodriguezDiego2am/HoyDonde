using FluentAssertions;
using HoyDonde.API.Data;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoyDonde.Tests.Services
{
    public class OrderServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly OrderRepository _orderRepository;
        private readonly ReservationRepository _reservationRepository;
        private readonly OrderService _orderService;
        private readonly ReservationService _reservationService;
        private readonly PricingService _pricingService;

        public OrderServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            _orderRepository = new OrderRepository(_context);
            _reservationRepository = new ReservationRepository(_context);
            _reservationService = new ReservationService(_context, _reservationRepository);
            _pricingService = new PricingService(_context);
            _orderService = new OrderService(
                _context,
                _orderRepository,
                _reservationService,
                _pricingService,
                NullLogger<OrderService>.Instance
            );

            SeedTestData();
        }

        private void SeedTestData()
        {
            var cliente = new Cliente
            {
                Id = "client1",
                UserName = "cliente1",
                Email = "cliente@test.com",
                FullName = "Cliente Test",
                DNI = "12345678"
            };

            var organizador = new Organizador
            {
                Id = "org1",
                UserName = "organizador1",
                Email = "org@test.com"
            };

            var evento = new Event
            {
                Id = 1,
                Nombre = "Evento Test",
                Fecha = DateTime.UtcNow.AddDays(30),
                Ubicacion = "Test Location",
                Descripcion = "Test Description",
                Categoria = Event.EventCategory.Musica,
                CapacidadMaxima = 100,
                OrganizadorId = "org1",
                Organizador = organizador
            };

            var ticketType = new TicketType
            {
                Id = 1,
                Nombre = "General",
                Precio = 100m,
                EventoId = 1,
                CantidadDisponible = 50
            };

            var serviceFee = new Fee
            {
                Id = 1,
                Nombre = "Comisión de servicio",
                Tipo = FeeType.ServiceFee,
                Porcentaje = 10m,
                MontoFijo = 0m,
                EsActivo = true,
                AplicaATodos = true,
                Orden = 1
            };

            _context.Clientes.Add(cliente);
            _context.Organizadores.Add(organizador);
            _context.Eventos.Add(evento);
            _context.TicketTypes.Add(ticketType);
            _context.Fees.Add(serviceFee);
            _context.SaveChanges();
        }

        [Fact]
        public async Task CreateOrderAsync_ShouldCreateValidOrder()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 2 }
                }
            };

            // Act
            var order = await _orderService.CreateOrderAsync(request);

            // Assert
            order.Should().NotBeNull();
            order.ClienteId.Should().Be("client1");
            order.Estado.Should().Be(OrderStatus.Pending);
            order.Items.Should().HaveCount(1);
            order.Items.First().Cantidad.Should().Be(2);
            order.OrderNumber.Should().NotBeNullOrEmpty();
            order.SubTotal.Should().Be(200m); // 2 * 100
            order.Total.Should().BeGreaterThan(order.SubTotal); // Debe incluir fees
        }

        [Fact]
        public async Task CreateOrderAsync_WithReservation_ShouldLinkReservation()
        {
            // Arrange
            var reservationItems = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 2 }
            };
            var reservation = await _reservationService.CreateReservationAsync("client1", reservationItems, 10);

            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                ReservationId = reservation.Id,
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 2 }
                }
            };

            // Act
            var order = await _orderService.CreateOrderAsync(request);

            // Assert
            order.ReservationId.Should().Be(reservation.Id);
        }

        [Fact]
        public async Task CreateOrderAsync_WhenInsufficientStock_ShouldThrowException()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 100 } // Más que el disponible
                }
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _orderService.CreateOrderAsync(request)
            );
        }

        [Fact]
        public async Task CompleteOrderAsync_ShouldGenerateTickets()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 3 }
                }
            };
            var order = await _orderService.CreateOrderAsync(request);

            // Simular que el pago fue procesado
            order.Estado = OrderStatus.Processing;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // Act
            var completedOrder = await _orderService.CompleteOrderAsync(order.Id);

            // Assert
            completedOrder.Estado.Should().Be(OrderStatus.Paid);
            completedOrder.Items.First().Tickets.Should().HaveCount(3);
            completedOrder.Items.First().Tickets.Should().OnlyContain(t => !string.IsNullOrEmpty(t.QrCode));
        }

        [Fact]
        public async Task CompleteOrderAsync_WhenNotProcessing_ShouldThrowException()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 1 }
                }
            };
            var order = await _orderService.CreateOrderAsync(request);

            // Act & Assert (orden está en Pending, no en Processing)
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _orderService.CompleteOrderAsync(order.Id)
            );
        }

        [Fact]
        public async Task CancelOrderAsync_ShouldUpdateStatusToCancelled()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 1 }
                }
            };
            var order = await _orderService.CreateOrderAsync(request);

            // Act
            await _orderService.CancelOrderAsync(order.Id);

            // Assert
            var cancelledOrder = await _orderService.GetOrderByIdAsync(order.Id);
            cancelledOrder.Should().NotBeNull();
            cancelledOrder!.Estado.Should().Be(OrderStatus.Cancelled);
        }

        [Fact]
        public async Task CancelOrderAsync_WhenAlreadyPaid_ShouldThrowException()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 1 }
                }
            };
            var order = await _orderService.CreateOrderAsync(request);
            order.Estado = OrderStatus.Paid;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _orderService.CancelOrderAsync(order.Id)
            );
        }

        [Fact]
        public async Task GetOrdersByClienteIdAsync_ShouldReturnClientOrders()
        {
            // Arrange
            var request1 = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 1 }
                }
            };
            var request2 = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 2 }
                }
            };

            await _orderService.CreateOrderAsync(request1);
            await _orderService.CreateOrderAsync(request2);

            // Act
            var orders = await _orderService.GetOrdersByClienteIdAsync("client1");

            // Assert
            orders.Should().HaveCount(2);
            orders.Should().OnlyContain(o => o.ClienteId == "client1");
        }

        [Fact]
        public async Task GetOrderSummaryAsync_ShouldReturnCompleteInformation()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 2 }
                }
            };
            var order = await _orderService.CreateOrderAsync(request);

            // Act
            var summary = await _orderService.GetOrderSummaryAsync(order.Id);

            // Assert
            summary.Should().NotBeNull();
            summary.OrderId.Should().Be(order.Id);
            summary.OrderNumber.Should().Be(order.OrderNumber);
            summary.Items.Should().HaveCount(1);
            summary.Items.First().TicketTypeName.Should().Be("General");
        }

        [Fact]
        public async Task ExpireOldOrdersAsync_ShouldExpirePendingOrders()
        {
            // Arrange
            var request = new CreateOrderRequest
            {
                ClienteId = "client1",
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { TicketTypeId = 1, Cantidad = 1 }
                }
            };
            var order = await _orderService.CreateOrderAsync(request);

            // Forzar que la orden sea antigua
            order.FechaCreacion = DateTime.UtcNow.AddMinutes(-20);
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // Act
            await _orderService.ExpireOldOrdersAsync();

            // Assert
            var expiredOrder = await _orderService.GetOrderByIdAsync(order.Id);
            expiredOrder.Should().NotBeNull();
            expiredOrder!.Estado.Should().Be(OrderStatus.Expired);
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
