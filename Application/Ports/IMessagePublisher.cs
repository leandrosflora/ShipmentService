namespace ShipmentService.Application.Ports;

public interface IMessagePublisher
{
    Task PublishAsync(string topic, string messageType, string aggregateKey, string payload, CancellationToken cancellationToken);
}
