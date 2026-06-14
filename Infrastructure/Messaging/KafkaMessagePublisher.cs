using System.Text.Json;
using Confluent.Kafka;
using ShipmentService.Application.Ports;

namespace ShipmentService.Infrastructure.Messaging;

public sealed class KafkaMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaMessagePublisher> _logger;

    public KafkaMessagePublisher(Microsoft.Extensions.Options.IOptions<KafkaOptions> options, ILogger<KafkaMessagePublisher> logger)
    {
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 3,
            SocketTimeoutMs = 5000,
            MessageTimeoutMs = 10000
        }).Build();
    }

    public async Task PublishAsync(string topic, string messageType, string aggregateKey, string payload, CancellationToken cancellationToken)
    {
        var (eventType, correlationId) = ExtractEnvelopeLogFields(payload, messageType);

        var result = await _producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = aggregateKey,
            Value = payload,
            Headers = new Headers
            {
                { "eventType", System.Text.Encoding.UTF8.GetBytes(eventType) },
                { "correlationId", System.Text.Encoding.UTF8.GetBytes(correlationId) }
            }
        }, cancellationToken);

        _logger.LogInformation(
            "Published Kafka message to topic {Topic} with key {MessageKey}, eventType {EventType} and correlationId {CorrelationId} at offset {Offset}",
            result.Topic,
            result.Message.Key,
            eventType,
            correlationId,
            result.Offset.Value);
    }

    public void Dispose() => _producer.Dispose();

    private static (string EventType, string CorrelationId) ExtractEnvelopeLogFields(string payload, string fallbackEventType)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("eventType", out var eventTypeProperty) ? eventTypeProperty.GetString() : fallbackEventType;
            var correlationId = root.TryGetProperty("correlationId", out var correlationIdProperty) ? correlationIdProperty.GetString() : string.Empty;
            return (eventType ?? fallbackEventType, correlationId ?? string.Empty);
        }
        catch (JsonException)
        {
            return (fallbackEventType, string.Empty);
        }
    }
}
