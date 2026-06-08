using Microsoft.EntityFrameworkCore;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Infrastructure.Outbox;

public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopeFactory, IMessagePublisher publisher, ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox dispatcher cycle failed");
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();

        var messages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await _publisher.PublishAsync(
                message.Topic,
                message.MessageType,
                message.AggregateKey,
                message.Payload,
                cancellationToken);

            message.MarkProcessed();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
