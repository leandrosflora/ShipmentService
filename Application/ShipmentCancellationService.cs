using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Application;

public sealed class ShipmentCancellationService
{
    private readonly ShipmentDbContext _dbContext;
    private readonly IOutboxWriter _outbox;

    public ShipmentCancellationService(ShipmentDbContext dbContext, IOutboxWriter outbox)
    {
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task RequestCancellationAsync(Guid shipmentId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var commandId = CreateCommandId(shipmentId, idempotencyKey);

        if (await _dbContext.InboxMessages.AnyAsync(x => x.MessageId == commandId, cancellationToken)) return;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var shipment = await _dbContext.Shipments.SingleOrDefaultAsync(x => x.Id == shipmentId, cancellationToken)
            ?? throw new KeyNotFoundException("Shipment not found");

        shipment.RequestCancellation();

        await _outbox.AddAsync(
            topic: "carrier-shipment.commands",
            aggregateKey: shipment.Id.ToString(),
            message: new
            {
                MessageId = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                shipment.CarrierCode,
                shipment.ExternalShipmentId,
                CommandType = "CancelCarrierShipment"
            },
            cancellationToken);

        await _dbContext.InboxMessages.AddAsync(new InboxMessage(commandId, "CancelShipmentCommand"), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Guid CreateCommandId(Guid shipmentId, string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{shipmentId}:{idempotencyKey}"));
        return new Guid(hash[..16]);
    }
}
