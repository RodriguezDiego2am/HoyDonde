using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    [Collection(FirestoreEmulatorCollection.Name)]
    public class PurchaseCheckoutEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;
        public PurchaseCheckoutEmulatorTests(FirestoreEmulatorFixture fixture) => _fixture = fixture;

        [FirestoreEmulatorFact]
        public async Task StartAsync_ConcurrentSameIdempotencyKeyCreatesExactlyOnePreference()
        {
            var (eventId, ticketTypeId) = await SeedEventAsync();
            var gateway = new DelayedPreferenceGateway { BlockCreation = true };
            var sut = new PurchaseCheckoutService(new PurchaseService(_fixture.Db!), gateway, _fixture.Db!);
            var request = new PurchaseStartRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 };

            var first = sut.StartAsync("customer", request, "same-key", CancellationToken.None);
            await gateway.FirstCallStarted;
            var second = sut.StartAsync("customer", request, "same-key", CancellationToken.None);
            await Task.Delay(100);
            Assert.Equal(1, gateway.CreateCalls);
            gateway.ReleaseCreation();
            var results = await Task.WhenAll(first, second);
            var persisted = (await _fixture.Db!.Collection("compras").Document(results[0].Id).GetSnapshotAsync()).ConvertTo<Compra>();

            Assert.Single(results.Select(result => result.Id).Distinct());
            Assert.Equal(1, gateway.CreateCalls);
            Assert.Equal(Compra.CheckoutStatus.Ready, persisted.EstadoCheckout);
            Assert.Equal("preference-1", persisted.CheckoutPreferenceId);
        }

        [FirestoreEmulatorFact]
        public async Task StartAsync_ZeroPriceConfirmsDirectlyWithoutCreatingCheckoutPreference()
        {
            var (eventId, ticketTypeId) = await SeedEventAsync(price: 0m);
            var gateway = new DelayedPreferenceGateway();
            var sut = new PurchaseCheckoutService(new PurchaseService(_fixture.Db!), gateway, _fixture.Db!);

            var result = await sut.StartAsync("customer-persona", new PurchaseStartRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 }, "free-key", CancellationToken.None);
            var persisted = (await _fixture.Db!.Collection("compras").Document(result.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            var tickets = (await _fixture.Db.Collection("tickets").GetSnapshotAsync()).Documents
                .Select(document => document.ConvertTo<Ticket>()).Where(ticket => ticket.CompraId == result.Id).ToList();

            Assert.Equal(Compra.PurchaseStatus.Confirmed, persisted.EstadoCompra);
            Assert.Equal(Compra.CheckoutStatus.NotStarted, persisted.EstadoCheckout);
            Assert.Equal(0, gateway.CreateCalls);
            Assert.Single(tickets);
            Assert.Equal("customer-persona", tickets[0].ClientePersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task StartAsync_AmbiguousCreationPersistsUnknownAndRepeatDoesNotBlindRetry()
        {
            var (eventId, ticketTypeId) = await SeedEventAsync();
            var gateway = new DelayedPreferenceGateway { AmbiguousFailure = true };
            var sut = new PurchaseCheckoutService(new PurchaseService(_fixture.Db!), gateway, _fixture.Db!);
            var request = new PurchaseStartRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 };

            var first = await sut.StartAsync("customer", request, "unknown-key", CancellationToken.None);
            var repeated = await sut.StartAsync("customer", request, "unknown-key", CancellationToken.None);
            var persisted = (await _fixture.Db!.Collection("compras").Document(first.Id).GetSnapshotAsync()).ConvertTo<Compra>();

            Assert.Equal(first.Id, repeated.Id);
            Assert.Equal(1, gateway.CreateCalls);
            Assert.Equal(Compra.CheckoutStatus.CreationUnknown, persisted.EstadoCheckout);
            Assert.Equal("CreationUnknown", repeated.CheckoutState);
        }

        private async Task<(string eventId, string ticketTypeId)> SeedEventAsync(decimal price = 100m)
        {
            var eventId = $"event-{Guid.NewGuid():N}";
            var ticketTypeId = $"type-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId, Nombre = "Evento", Ubicacion = "Buenos Aires",
                FechaInicio = DateTime.UtcNow.AddDays(2),
                FechaFin = DateTime.UtcNow.AddDays(3), Estado = Event.EventStatus.Publicado,
                TicketTypes = new List<TicketType> { new() { Id = ticketTypeId, Nombre = "General", Precio = price, CantidadDisponible = 3, EventoId = eventId } },
            });
            return (eventId, ticketTypeId);
        }

        private sealed class DelayedPreferenceGateway : IMercadoPagoGateway
        {
            private int _createCalls;
            private readonly TaskCompletionSource<bool> _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _releaseCreation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int CreateCalls => _createCalls;
            public bool AmbiguousFailure { get; init; }
            public bool BlockCreation { get; init; }
            public Task FirstCallStarted => _firstCallStarted.Task;
            public void ReleaseCreation() => _releaseCreation.TrySetResult(true);
            public async Task<MercadoPagoPreference> CreatePreferenceAsync(MercadoPagoPreferenceRequest request, CancellationToken cancellationToken)
            {
                var call = Interlocked.Increment(ref _createCalls);
                if (call == 1) _firstCallStarted.TrySetResult(true);
                if (BlockCreation) await _releaseCreation.Task.WaitAsync(cancellationToken);
                if (AmbiguousFailure) throw new HttpRequestException("timeout after request was sent");
                return new MercadoPagoPreference("preference-1", "https://checkout.example/preference-1");
            }
            public Task<MercadoPagoPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<MercadoPagoRefund> RefundFullAsync(string paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        }
    }
}
