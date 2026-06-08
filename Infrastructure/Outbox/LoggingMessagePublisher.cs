using ShipmentService.Application.Ports;

namespace ShipmentService.Infrastructure.Outbox;

public sealed class LoggingMessagePublisher : IMessagePublisher
{
    private readonly ILogger<LoggingMessagePublisher> _logger;

    public LoggingMessagePublisher(ILogger<LoggingMessagePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(string topic, string messageType, string aggregateKey, string payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Publishing {MessageType} to {Topic} for aggregate {AggregateKey}",
            messageType,
            topic,
            aggregateKey);

        return Task.CompletedTask;
    }
}
