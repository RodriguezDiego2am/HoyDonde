using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Services
{
    // Short independent loops keep webhook ACK latency separate from payment, expiry, and refund work.
    public sealed class MercadoPagoInboxWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes; private readonly ILogger<MercadoPagoInboxWorker> _logger;
        public MercadoPagoInboxWorker(IServiceScopeFactory scopes, ILogger<MercadoPagoInboxWorker> logger) { _scopes = scopes; _logger = logger; }
        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { using var scope = _scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<IPaymentProcessingService>().ProcessPendingInboxAsync(token); }
                catch (Exception exception) { _logger.LogError(exception, "Mercado Pago inbox worker cycle failed."); }
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
        }
    }

    public sealed class MercadoPagoReservationExpiryWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        public MercadoPagoReservationExpiryWorker(IServiceScopeFactory scopes) => _scopes = scopes;
        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPaymentProcessingService>().ExpireDueReservationsAsync(token);
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }
        }
    }

    public sealed class MercadoPagoRefundWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        public MercadoPagoRefundWorker(IServiceScopeFactory scopes) => _scopes = scopes;
        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPaymentProcessingService>().ProcessRequiredRefundsAsync(token);
                await Task.Delay(TimeSpan.FromSeconds(20), token);
            }
        }
    }
}
