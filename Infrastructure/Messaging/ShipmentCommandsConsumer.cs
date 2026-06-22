using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShipmentService.Application.Ports;
using ShipmentService.Contracts;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Infrastructure.Messaging;

public sealed class ShipmentCommandsConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<ShipmentCommandsConsumer> _logger;

    public ShipmentCommandsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<ShipmentCommandsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.Topics.ShipmentCommands);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var cmd = JsonSerializer.Deserialize<SagaCreateShipmentCommand>(result.Message.Value, JsonOptions);

                if (cmd is not null && cmd.OrderId != Guid.Empty)
                {
                    await HandleCreateShipmentCommandAsync(cmd, stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Received unrecognized or empty shipment command; skipping");
                }

                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume topic {Topic}", _options.Topics.ShipmentCommands);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task HandleCreateShipmentCommandAsync(SagaCreateShipmentCommand cmd, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        var shipment = await dbContext.Shipments
            .FirstOrDefaultAsync(x => x.OrderId == cmd.OrderId, cancellationToken);

        if (shipment is null)
        {
            _logger.LogWarning("No shipment found for order {OrderId} when processing shipment.commands; shipment not yet created from order.created", cmd.OrderId);
            return;
        }

        var shipmentCreatedEvent = new EventEnvelope<ShipmentCreatedIntegrationEvent>(
            Guid.NewGuid(),
            "shipment.created",
            "1.0",
            DateTimeOffset.UtcNow,
            cmd.MessageId.ToString(),
            "shipment-service",
            new ShipmentCreatedIntegrationEvent(
                shipment.Id,
                shipment.OrderId,
                shipment.BuyerId,
                shipment.CarrierCode,
                shipment.ServiceLevelCode,
                shipment.ExternalShipmentId ?? $"ext-{shipment.Id:N}",
                shipment.TrackingCode ?? $"TRACK-{shipment.Id:N}",
                shipment.LabelObjectKey ?? $"labels/{shipment.Id:N}.pdf",
                shipment.PromisedDeliveryDate,
                shipment.CreatedAt));

        await outbox.AddAsync(_options.Topics.ShipmentCreated, shipment.OrderId.ToString(), shipmentCreatedEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Re-published shipment.created for order {OrderId} from shipment.commands trigger", cmd.OrderId);
    }
}

internal sealed record SagaCreateShipmentCommand(
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("shippingPromiseId")] string? ShippingPromiseId,
    [property: JsonPropertyName("inventoryReservationId")] Guid InventoryReservationId,
    [property: JsonPropertyName("capacityReservationId")] Guid CapacityReservationId);
