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
        ValidatePayload(payload, envelope.EventId);

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

    private void ValidatePayload(OrderCreatedIntegrationEvent payload, Guid eventId)
    {
        var missingFields = new List<string>();

        if (payload.OrderId == Guid.Empty) missingFields.Add(nameof(payload.OrderId));
        if (payload.BuyerId == Guid.Empty) missingFields.Add(nameof(payload.BuyerId));
        if (payload.SellerId == Guid.Empty) missingFields.Add(nameof(payload.SellerId));
        if (string.IsNullOrWhiteSpace(payload.ShippingPromiseId)) missingFields.Add(nameof(payload.ShippingPromiseId));
        if (string.IsNullOrWhiteSpace(payload.RouteId)) missingFields.Add(nameof(payload.RouteId));
        if (string.IsNullOrWhiteSpace(payload.CarrierCode)) missingFields.Add(nameof(payload.CarrierCode));
        if (string.IsNullOrWhiteSpace(payload.ServiceLevelCode)) missingFields.Add(nameof(payload.ServiceLevelCode));
        if (payload.OriginNodeId == Guid.Empty) missingFields.Add(nameof(payload.OriginNodeId));
        if (payload.PromisedDeliveryDate == default) missingFields.Add(nameof(payload.PromisedDeliveryDate));
        if (payload.Destination is null)
        {
            missingFields.Add(nameof(payload.Destination));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(payload.Destination.Street)) missingFields.Add("Destination.Street");
            if (string.IsNullOrWhiteSpace(payload.Destination.Number)) missingFields.Add("Destination.Number");
            if (string.IsNullOrWhiteSpace(payload.Destination.City)) missingFields.Add("Destination.City");
            if (string.IsNullOrWhiteSpace(payload.Destination.State)) missingFields.Add("Destination.State");
            if (string.IsNullOrWhiteSpace(payload.Destination.PostalCode)) missingFields.Add("Destination.ZipCode");
            if (string.IsNullOrWhiteSpace(payload.Destination.Country)) missingFields.Add("Destination.Country");
        }

        if (payload.Packages is null || payload.Packages.Count == 0)
        {
            missingFields.Add(nameof(payload.Packages));
        }
        else
        {
            for (var index = 0; index < payload.Packages.Count; index++)
            {
                var package = payload.Packages[index];
                if (package.WeightKg <= 0) missingFields.Add($"Packages[{index}].WeightKg");
                if (package.HeightCm <= 0) missingFields.Add($"Packages[{index}].HeightCm");
                if (package.WidthCm <= 0) missingFields.Add($"Packages[{index}].WidthCm");
                if (package.LengthCm <= 0) missingFields.Add($"Packages[{index}].LengthCm");
                if (package.Items is null || package.Items.Count == 0) missingFields.Add($"Packages[{index}].Items");
            }
        }

        if (missingFields.Count == 0) return;

        _logger.LogError(
            "Cannot create shipment from order.created event {EventId}; missing or invalid required fields: {MissingFields}",
            eventId,
            string.Join(", ", missingFields));

        throw new InvalidOperationException($"order.created event {eventId} is missing required fields: {string.Join(", ", missingFields)}");
    }
}
