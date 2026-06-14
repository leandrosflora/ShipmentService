using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShipmentService.Contracts;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.Messaging;
using ShipmentService.Domain;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Application;

public sealed class ShipmentCreationHandler
{
    private readonly ShipmentDbContext _dbContext;
    private readonly IOutboxWriter _outbox;
    private readonly KafkaOptions _kafkaOptions;

    public ShipmentCreationHandler(ShipmentDbContext dbContext, IOutboxWriter outbox, IOptions<KafkaOptions> kafkaOptions)
    {
        _dbContext = dbContext;
        _outbox = outbox;
        _kafkaOptions = kafkaOptions.Value;
    }

    public Task HandleAsync(CreateShipmentCommand command, CancellationToken cancellationToken) =>
        HandleAsync(command, command.MessageId.ToString(), cancellationToken);

    public async Task HandleAsync(CreateShipmentCommand command, string correlationId, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await _dbContext.InboxMessages.AnyAsync(x => x.MessageId == command.MessageId, cancellationToken);
        if (alreadyProcessed) return;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingShipment = await _dbContext.Shipments.SingleOrDefaultAsync(x => x.ShipmentRequestId == command.ShipmentRequestId || x.OrderId == command.OrderId, cancellationToken);

        if (existingShipment is null)
        {
            var address = new ShipmentAddress(
                command.Destination.RecipientName,
                command.Destination.Street,
                command.Destination.Number,
                command.Destination.Complement,
                command.Destination.District,
                command.Destination.City,
                command.Destination.State,
                command.Destination.PostalCode,
                command.Destination.Country,
                command.Destination.Phone);

            var packages = command.Packages.Select(package => new ShipmentPackage(
                package.Sequence,
                package.WeightKg,
                package.HeightCm,
                package.WidthCm,
                package.LengthCm,
                package.Items.Select(item => new ShipmentPackageItem(item.SkuId, item.Quantity)))).ToList();

            var shipment = Shipment.Create(
                command.ShipmentRequestId,
                command.OrderId,
                command.BuyerId,
                command.SellerId,
                command.ShippingPromiseId,
                command.RouteId,
                command.CarrierCode,
                command.ServiceLevelCode,
                command.OriginNodeId,
                address,
                command.PromisedDeliveryDate,
                packages);

            await _dbContext.Shipments.AddAsync(shipment, cancellationToken);

            var shipmentCreated = new EventEnvelope<ShipmentCreatedIntegrationEvent>(
                Guid.NewGuid(),
                "shipment.created",
                "1.0",
                DateTimeOffset.UtcNow,
                correlationId,
                "shipment-service",
                new ShipmentCreatedIntegrationEvent(
                    Guid.NewGuid(),
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CarrierCode,
                    shipment.ServiceLevelCode,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    shipment.PromisedDeliveryDate,
                    DateTimeOffset.UtcNow));

            await _outbox.AddAsync(_kafkaOptions.Topics.ShipmentCreated, shipment.OrderId.ToString(), shipmentCreated, cancellationToken);
        }

        await _dbContext.InboxMessages.AddAsync(new InboxMessage(command.MessageId, nameof(OrderCreatedIntegrationEvent)), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
