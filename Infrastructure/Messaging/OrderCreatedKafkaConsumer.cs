using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using ShipmentService.Application;
using ShipmentService.Contracts;

namespace ShipmentService.Infrastructure.Messaging;

public sealed class OrderCreatedKafkaConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<OrderCreatedKafkaConsumer> _logger;

    public OrderCreatedKafkaConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger<OrderCreatedKafkaConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            SocketTimeoutMs = 5000
        }).Build();

        consumer.Subscribe(_options.Topics.OrderCreated);
        _logger.LogInformation("Kafka consumer subscribed to topic {Topic} with group {ConsumerGroupId}", _options.Topics.OrderCreated, _options.ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                await HandleMessageAsync(result, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException exception)
            {
                _logger.LogError(exception, "Kafka consume failed for topic {Topic}", _options.Topics.OrderCreated);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Kafka order.created handler failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task HandleMessageAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderCreatedIntegrationEvent>>(result.Message.Value, SerializerOptions)
            ?? throw new InvalidOperationException("Invalid order.created envelope");

        if (!string.Equals(envelope.EventType, "order.created", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected eventType '{envelope.EventType}' on order.created consumer");
        }

        _logger.LogInformation(
            "Consumed Kafka message from topic {Topic} with key {MessageKey}, eventType {EventType} and correlationId {CorrelationId}",
            result.Topic,
            result.Message.Key,
            envelope.EventType,
            envelope.CorrelationId);

        var payload = envelope.Payload;
        var command = new CreateShipmentCommand(
            envelope.EventId,
            payload.OrderId,
            payload.OrderId,
            payload.BuyerId,
            payload.SellerId,
            payload.ShippingPromiseId,
            payload.RouteId,
            payload.CarrierCode,
            payload.ServiceLevelCode,
            payload.OriginNodeId,
            payload.PromisedDeliveryDate,
            payload.Destination,
            payload.Packages);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ShipmentCreationHandler>();
        await handler.HandleAsync(command, envelope.CorrelationId, cancellationToken);
    }
}
