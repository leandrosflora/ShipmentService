namespace ShipmentService.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Topic { get; private set; } = default!;
    public string MessageType { get; private set; } = default!;
    public string AggregateKey { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    private OutboxMessage() { }

    public OutboxMessage(string topic, string messageType, string aggregateKey, string payload)
    {
        Id = Guid.NewGuid();
        Topic = topic;
        MessageType = messageType;
        AggregateKey = aggregateKey;
        Payload = payload;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;
}
