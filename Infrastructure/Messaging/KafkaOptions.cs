namespace ShipmentService.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; init; } = "localhost:9092";
    public string ConsumerGroupId { get; init; } = "shipment-service";
    public KafkaTopicsOptions Topics { get; init; } = new();
}

public sealed class KafkaTopicsOptions
{
    public string OrderCreated { get; init; } = "order.created";
    public string ShipmentCreated { get; init; } = "shipment.created";
}
