using FluentAssertions;
using HoyDonde.API.Data;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoyDonde.Tests.Services
{
    public class PricingServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly PricingService _pricingService;

        public PricingServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            _pricingService = new PricingService(_context);

            SeedTestData();
        }

        private void SeedTestData()
        {
            // Crear evento de prueba
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

            var ticketType1 = new TicketType
            {
                Id = 1,
                Nombre = "General",
                Precio = 100m,
                EventoId = 1,
                CantidadDisponible = 50
            };

            var ticketType2 = new TicketType
            {
                Id = 2,
                Nombre = "VIP",
                Precio = 200m,
                EventoId = 1,
                CantidadDisponible = 20
            };

            // Crear fees de prueba
            var serviceFee = new Fee
            {
                Id = 1,
                Nombre = "Comisión de servicio",
                Tipo = FeeType.ServiceFee,
                Porcentaje = 10m, // 10%
                MontoFijo = 0m,
                EsActivo = true,
                AplicaATodos = true,
                Orden = 1
            };

            var tax = new Fee
            {
                Id = 2,
                Nombre = "IVA",
                Tipo = FeeType.Tax,
                Porcentaje = 21m, // 21%
                MontoFijo = 0m,
                EsActivo = true,
                AplicaATodos = true,
                Orden = 2
            };

            _context.Organizadores.Add(organizador);
            _context.Eventos.Add(evento);
            _context.TicketTypes.AddRange(ticketType1, ticketType2);
            _context.Fees.AddRange(serviceFee, tax);
            _context.SaveChanges();
        }

        [Fact]
        public async Task CalculatePricingAsync_ShouldCalculateCorrectSubtotal()
        {
            // Arrange
            var items = new List<OrderItemRequest>
            {
                new OrderItemRequest { TicketTypeId = 1, Cantidad = 2 }, // 2 x 100 = 200
                new OrderItemRequest { TicketTypeId = 2, Cantidad = 1 }  // 1 x 200 = 200
            };

            // Act
            var result = await _pricingService.CalculatePricingAsync(items);

            // Assert
            result.SubTotal.Should().Be(400m); // 200 + 200 = 400
        }

        [Fact]
        public async Task CalculatePricingAsync_ShouldCalculateFeesCorrectly()
        {
            // Arrange
            var items = new List<OrderItemRequest>
            {
                new OrderItemRequest { TicketTypeId = 1, Cantidad = 2 } // 2 x 100 = 200
            };

            // Act
            var result = await _pricingService.CalculatePricingAsync(items);

            // Assert
            result.SubTotal.Should().Be(200m);
            result.Fees.Should().Be(20m); // 10% de 200 = 20
            result.Taxes.Should().Be(42m); // 21% de 200 = 42
            result.Total.Should().Be(262m); // 200 + 20 + 42 = 262
        }

        [Fact]
        public async Task CalculatePricingAsync_WithMultipleItems_ShouldCalculateCorrectTotal()
        {
            // Arrange
            var items = new List<OrderItemRequest>
            {
                new OrderItemRequest { TicketTypeId = 1, Cantidad = 3 }, // 3 x 100 = 300
                new OrderItemRequest { TicketTypeId = 2, Cantidad = 2 }  // 2 x 200 = 400
            };

            // Act
            var result = await _pricingService.CalculatePricingAsync(items);

            // Assert
            result.SubTotal.Should().Be(700m); // 300 + 400 = 700
            result.Fees.Should().Be(70m); // 10% de 700 = 70
            result.Taxes.Should().Be(147m); // 21% de 700 = 147
            result.Total.Should().Be(917m); // 700 + 70 + 147 = 917
        }

        [Fact]
        public async Task CalculatePricingAsync_ShouldIncludeBreakdown()
        {
            // Arrange
            var items = new List<OrderItemRequest>
            {
                new OrderItemRequest { TicketTypeId = 1, Cantidad = 1 }
            };

            // Act
            var result = await _pricingService.CalculatePricingAsync(items);

            // Assert
            result.Breakdown.Should().NotBeEmpty();
            result.Breakdown.Should().Contain(b => b.Tipo == "subtotal");
            result.Breakdown.Should().Contain(b => b.Tipo == "fee");
            result.Breakdown.Should().Contain(b => b.Tipo == "tax");
        }

        [Fact]
        public async Task CalculatePricingAsync_WithInvalidTicketType_ShouldThrowException()
        {
            // Arrange
            var items = new List<OrderItemRequest>
            {
                new OrderItemRequest { TicketTypeId = 999, Cantidad = 1 } // ID inexistente
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => _pricingService.CalculatePricingAsync(items)
            );
        }

        [Fact]
        public async Task GetActiveFeesAsync_ShouldReturnOnlyActiveFees()
        {
            // Arrange
            var inactiveFee = new Fee
            {
                Id = 3,
                Nombre = "Fee inactivo",
                Tipo = FeeType.ServiceFee,
                Porcentaje = 5m,
                EsActivo = false,
                AplicaATodos = true,
                Orden = 3
            };
            _context.Fees.Add(inactiveFee);
            await _context.SaveChangesAsync();

            // Act
            var result = await _pricingService.GetActiveFeesAsync();

            // Assert
            result.Should().NotBeEmpty();
            result.Should().OnlyContain(f => f.EsActivo);
            result.Should().NotContain(f => f.Id == 3);
        }

        [Fact]
        public async Task CalculatePricingAsync_WithZeroQuantity_ShouldReturnZeroSubtotal()
        {
            // Arrange
            var items = new List<OrderItemRequest>();

            // Act
            var result = await _pricingService.CalculatePricingAsync(items);

            // Assert
            result.SubTotal.Should().Be(0m);
            result.Total.Should().Be(0m);
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
