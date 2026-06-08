namespace ShipmentService.Application.Ports;

public interface IOutboxWriter
{
    Task AddAsync<TMessage>(string topic, string aggregateKey, TMessage message, CancellationToken cancellationToken) where TMessage : notnull;
}
