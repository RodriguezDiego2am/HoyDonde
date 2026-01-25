using HoyDonde.API.Services;

namespace HoyDonde.API.BackgroundJobs
{
    public class ReservationCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReservationCleanupService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(1); // Ejecutar cada minuto

        public ReservationCleanupService(
            IServiceProvider serviceProvider,
            ILogger<ReservationCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReservationCleanupService iniciado");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DoWorkAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en ReservationCleanupService");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("ReservationCleanupService detenido");
        }

        private async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();

            var reservationService = scope.ServiceProvider.GetRequiredService<IReservationService>();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

            // Limpiar reservas expiradas
            await reservationService.ReleaseExpiredReservationsAsync();

            // Expirar órdenes antiguas pendientes
            await orderService.ExpireOldOrdersAsync();
        }
    }
}
