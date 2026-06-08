using System.Text.Json;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Infrastructure.Outbox;

public sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ShipmentDbContext _dbContext;

    public OutboxWriter(ShipmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync<TMessage>(string topic, string aggregateKey, TMessage message, CancellationToken cancellationToken) where TMessage : notnull
    {
        var outboxMessage = new OutboxMessage(
            topic,
            typeof(TMessage).Name,
            aggregateKey,
            JsonSerializer.Serialize(message, SerializerOptions));

        await _dbContext.OutboxMessages.AddAsync(outboxMessage, cancellationToken);
    }
}
