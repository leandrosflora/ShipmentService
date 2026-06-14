using Microsoft.EntityFrameworkCore;
using ShipmentService.Application.Ports;
using ShipmentService.Contracts;
using ShipmentService.Domain;
using ShipmentService.Infrastructure.Carrier;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Application;

public sealed class CarrierBookingProcessor
{
    private const int MaximumAttempts = 8;
    private readonly ShipmentDbContext _dbContext;
    private readonly ICarrierShipmentClient _carrierClient;
    private readonly ILabelStorage _labelStorage;
    private readonly IOutboxWriter _outbox;
    private readonly ILogger<CarrierBookingProcessor> _logger;

    public CarrierBookingProcessor(ShipmentDbContext dbContext, ICarrierShipmentClient carrierClient, ILabelStorage labelStorage, IOutboxWriter outbox, ILogger<CarrierBookingProcessor> logger)
    {
        _dbContext = dbContext;
        _carrierClient = carrierClient;
        _labelStorage = labelStorage;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Shipments
            .Include(x => x.Packages).ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == shipmentId, cancellationToken);

        if (shipment is null || shipment.Status == ShipmentStatus.ReadyToShip || shipment.Status != ShipmentStatus.BookingInProgress) return;

        try
        {
            var carrierResponse = await _carrierClient.CreateAsync(MapRequest(shipment), shipment.Id.ToString("N"), cancellationToken);
            var labelBytes = Convert.FromBase64String(carrierResponse.LabelContentBase64);
            var storedLabel = await _labelStorage.StoreAsync(shipment.Id, labelBytes, carrierResponse.LabelMimeType, cancellationToken);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            shipment.MarkReady(carrierResponse.ExternalShipmentId, carrierResponse.TrackingCode, storedLabel.ObjectKey, storedLabel.Sha256);

            await _outbox.AddAsync(
                topic: "shipment.events",
                aggregateKey: shipment.OrderId.ToString(),
                message: new EventEnvelope<ShipmentCreatedIntegrationEvent>(
                    Guid.NewGuid(),
                    "shipment.created",
                    "1.0",
                    DateTimeOffset.UtcNow,
                    shipment.OrderId.ToString(),
                    "shipment-service",
                    new ShipmentCreatedIntegrationEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.BuyerId,
                    shipment.CarrierCode,
                    shipment.ServiceLevelCode,
                    carrierResponse.ExternalShipmentId,
                    carrierResponse.TrackingCode,
                    storedLabel.ObjectKey,
                    shipment.PromisedDeliveryDate,
                    DateTimeOffset.UtcNow)),
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PermanentCarrierException exception)
        {
            await FailPermanentlyAsync(shipment, exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Carrier booking failed for shipment {ShipmentId}", shipment.Id);

            if (shipment.BookingAttempts >= MaximumAttempts)
            {
                await FailPermanentlyAsync(shipment, "Maximum booking attempts exceeded", cancellationToken);
                return;
            }

            shipment.ScheduleRetry(exception.Message, DateTimeOffset.UtcNow.Add(CalculateRetryDelay(shipment.BookingAttempts)));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task FailPermanentlyAsync(Shipment shipment, string reason, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        shipment.MarkFailed(reason);
        await _outbox.AddAsync(
            topic: "shipment.events",
            aggregateKey: shipment.OrderId.ToString(),
            message: new ShipmentCreationFailedIntegrationEvent(Guid.NewGuid(), shipment.Id, shipment.OrderId, shipment.CarrierCode, reason, DateTimeOffset.UtcNow),
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static TimeSpan CalculateRetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempt)));

    private static CreateCarrierShipmentRequest MapRequest(Shipment shipment)
    {
        var destination = shipment.Destination;
        return new CreateCarrierShipmentRequest(
            shipment.Id,
            shipment.CarrierCode,
            shipment.ServiceLevelCode,
            shipment.RouteId,
            shipment.OriginNodeId,
            new ShipmentAddressDto(destination.Street, destination.Number, destination.City, destination.State, destination.PostalCode, destination.Country, destination.RecipientName, destination.Complement, destination.District, destination.Phone),
            shipment.Packages.Select(package => new CarrierPackageDto(
                package.Sequence,
                package.WeightKg,
                package.HeightCm,
                package.WidthCm,
                package.LengthCm,
                package.Items.Select(item => new CarrierPackageItemDto(item.SkuId, item.Quantity)).ToList())).ToList());
    }
}
