using FluentAssertions;
using HoyDonde.API.Data;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HoyDonde.Tests.Services
{
    public class ReservationServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly ReservationRepository _reservationRepository;
        private readonly ReservationService _reservationService;

        public ReservationServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            _reservationRepository = new ReservationRepository(_context);
            _reservationService = new ReservationService(_context, _reservationRepository);

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

            _context.Clientes.Add(cliente);
            _context.Organizadores.Add(organizador);
            _context.Eventos.Add(evento);
            _context.TicketTypes.Add(ticketType);
            _context.SaveChanges();
        }

        [Fact]
        public async Task CreateReservationAsync_ShouldCreateValidReservation()
        {
            // Arrange
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 2 }
            };

            // Act
            var reservation = await _reservationService.CreateReservationAsync("client1", items, 10);

            // Assert
            reservation.Should().NotBeNull();
            reservation.ClienteId.Should().Be("client1");
            reservation.Estado.Should().Be(ReservationStatus.Active);
            reservation.Items.Should().HaveCount(1);
            reservation.Items.First().Cantidad.Should().Be(2);
            reservation.FechaExpiracion.Should().BeAfter(DateTime.UtcNow);
        }

        [Fact]
        public async Task CreateReservationAsync_WhenInsufficientStock_ShouldThrowException()
        {
            // Arrange
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 100 } // Más que el disponible (50)
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _reservationService.CreateReservationAsync("client1", items, 10)
            );
        }

        [Fact]
        public async Task HasAvailableStockAsync_WithAvailableStock_ShouldReturnTrue()
        {
            // Arrange & Act
            var hasStock = await _reservationService.HasAvailableStockAsync(1, 10);

            // Assert
            hasStock.Should().BeTrue();
        }

        [Fact]
        public async Task HasAvailableStockAsync_WithInsufficientStock_ShouldReturnFalse()
        {
            // Arrange & Act
            var hasStock = await _reservationService.HasAvailableStockAsync(1, 100);

            // Assert
            hasStock.Should().BeFalse();
        }

        [Fact]
        public async Task HasAvailableStockAsync_ShouldConsiderActiveReservations()
        {
            // Arrange
            // Crear una reserva activa de 40 entradas
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 40 }
            };
            await _reservationService.CreateReservationAsync("client1", items, 10);

            // Act - Intentar verificar si hay stock para 20 más (total 60, pero capacidad es 50)
            var hasStock = await _reservationService.HasAvailableStockAsync(1, 20);

            // Assert
            hasStock.Should().BeFalse(); // 40 reservados + 20 solicitados = 60 > 50 disponibles
        }

        [Fact]
        public async Task ValidateReservationAsync_WithValidReservation_ShouldReturnTrue()
        {
            // Arrange
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 2 }
            };
            var reservation = await _reservationService.CreateReservationAsync("client1", items, 10);

            // Act
            var isValid = await _reservationService.ValidateReservationAsync(reservation.Id);

            // Assert
            isValid.Should().BeTrue();
        }

        [Fact]
        public async Task ValidateReservationAsync_WithCancelledReservation_ShouldReturnFalse()
        {
            // Arrange
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 2 }
            };
            var reservation = await _reservationService.CreateReservationAsync("client1", items, 10);
            await _reservationService.CancelReservationAsync(reservation.Id);

            // Act
            var isValid = await _reservationService.ValidateReservationAsync(reservation.Id);

            // Assert
            isValid.Should().BeFalse();
        }

        [Fact]
        public async Task CompleteReservationAsync_ShouldUpdateStatusToCompleted()
        {
            // Arrange
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 2 }
            };
            var reservation = await _reservationService.CreateReservationAsync("client1", items, 10);

            // Act
            await _reservationService.CompleteReservationAsync(reservation.Id, 1);

            // Assert
            var updatedReservation = await _reservationService.GetReservationByIdAsync(reservation.Id);
            updatedReservation.Should().NotBeNull();
            updatedReservation!.Estado.Should().Be(ReservationStatus.Completed);
            updatedReservation.OrderId.Should().Be(1);
        }

        [Fact]
        public async Task CancelReservationAsync_ShouldUpdateStatusToCancelled()
        {
            // Arrange
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 2 }
            };
            var reservation = await _reservationService.CreateReservationAsync("client1", items, 10);

            // Act
            await _reservationService.CancelReservationAsync(reservation.Id);

            // Assert
            var updatedReservation = await _reservationService.GetReservationByIdAsync(reservation.Id);
            updatedReservation.Should().NotBeNull();
            updatedReservation!.Estado.Should().Be(ReservationStatus.Cancelled);
        }

        [Fact]
        public async Task ReleaseExpiredReservationsAsync_ShouldExpireOldReservations()
        {
            // Arrange
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 2 }
            };
            var reservation = await _reservationService.CreateReservationAsync("client1", items, 1);

            // Forzar la expiración modificando la fecha manualmente
            reservation.FechaExpiracion = DateTime.UtcNow.AddMinutes(-1);
            _context.Reservations.Update(reservation);
            await _context.SaveChangesAsync();

            // Act
            await _reservationService.ReleaseExpiredReservationsAsync();

            // Assert
            var updatedReservation = await _reservationService.GetReservationByIdAsync(reservation.Id);
            updatedReservation.Should().NotBeNull();
            updatedReservation!.Estado.Should().Be(ReservationStatus.Expired);
        }

        [Fact]
        public async Task HasAvailableStockAsync_ShouldNotConsiderExpiredReservations()
        {
            // Arrange
            // Crear una reserva que va a expirar
            var items = new List<ReservationItemRequest>
            {
                new ReservationItemRequest { TicketTypeId = 1, Cantidad = 40 }
            };
            var reservation = await _reservationService.CreateReservationAsync("client1", items, 1);

            // Forzar la expiración
            reservation.FechaExpiracion = DateTime.UtcNow.AddMinutes(-1);
            _context.Reservations.Update(reservation);
            await _context.SaveChangesAsync();

            // Act - Ahora debería haber stock disponible porque la reserva expiró
            var hasStock = await _reservationService.HasAvailableStockAsync(1, 40);

            // Assert
            hasStock.Should().BeTrue(); // La reserva expirada no cuenta
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
