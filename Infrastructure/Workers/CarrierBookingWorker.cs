using ShipmentService.Application;
using ShipmentService.Application.Ports;

namespace ShipmentService.Infrastructure.Workers;

public sealed class CarrierBookingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CarrierBookingWorker> _logger;

    public CarrierBookingWorker(IServiceScopeFactory scopeFactory, ILogger<CarrierBookingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Carrier booking worker cycle failed");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> shipmentIds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IShipmentRepository>();
            shipmentIds = await repository.ClaimBookableAsync(20, cancellationToken);
        }

        await Parallel.ForEachAsync(
            shipmentIds,
            new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = cancellationToken },
            async (shipmentId, token) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<CarrierBookingProcessor>();
                await processor.ProcessAsync(shipmentId, token);
            });
    }
}
