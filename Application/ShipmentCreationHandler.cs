using Microsoft.EntityFrameworkCore;
using ShipmentService.Contracts;
using ShipmentService.Domain;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Application;

public sealed class ShipmentCreationHandler
{
    private readonly ShipmentDbContext _dbContext;

    public ShipmentCreationHandler(ShipmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task HandleAsync(CreateShipmentCommand command, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await _dbContext.InboxMessages.AnyAsync(x => x.MessageId == command.MessageId, cancellationToken);
        if (alreadyProcessed) return;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingShipment = await _dbContext.Shipments.SingleOrDefaultAsync(x => x.ShipmentRequestId == command.ShipmentRequestId, cancellationToken);

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
        }

        await _dbContext.InboxMessages.AddAsync(new InboxMessage(command.MessageId, nameof(CreateShipmentCommand)), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
