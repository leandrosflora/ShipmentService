using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShipmentService.Application;
using ShipmentService.Application.Ports;
using ShipmentService.Contracts;
using ShipmentService.Infrastructure.Messaging;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.UnitTest.Application;

public class ShipmentCreationHandlerTests
{
    static ShipmentDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ShipmentDbContext(options);
    }

    static CreateShipmentCommand ValidCommand(Guid? orderId = null, Guid? shipmentRequestId = null) => new(
        MessageId: Guid.NewGuid(),
        ShipmentRequestId: shipmentRequestId ?? Guid.NewGuid(),
        OrderId: orderId ?? Guid.NewGuid(),
        BuyerId: Guid.NewGuid(),
        SellerId: Guid.NewGuid(),
        ShippingPromiseId: "promise-1",
        RouteId: "route-1",
        CarrierCode: "CORREIOS",
        ServiceLevelCode: "PAC",
        OriginNodeId: Guid.NewGuid(),
        PromisedDeliveryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
        Destination: new ShipmentAddressDto(
            "Rua das Flores", "100", "São Paulo", "SP", "01310-100", "BR",
            "João Silva", null, "Centro", "11999999999"),
        Packages: [new CreateShipmentPackageDto(1.5m, 20m, 15m, 10m, [new CreateShipmentPackageItemDto(Guid.NewGuid(), 1)], null, 1)]);

    [Fact]
    public async Task HandleAsync_NewCommand_CreatesShipmentAndInboxMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var outbox = Substitute.For<IOutboxWriter>();
        var handler = new ShipmentCreationHandler(ctx, outbox, Options.Create(new KafkaOptions()));

        await handler.HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, await ctx.Shipments.CountAsync());
        Assert.Equal(1, await ctx.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_SameMessageId_IsIdempotent()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var outbox = Substitute.For<IOutboxWriter>();
        var handler = new ShipmentCreationHandler(ctx, outbox, Options.Create(new KafkaOptions()));
        var command = ValidCommand();

        await handler.HandleAsync(command, CancellationToken.None);
        await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, await ctx.Shipments.CountAsync());
        Assert.Equal(1, await ctx.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_ExistingShipmentForOrder_SkipsCreationButSavesInboxMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        var orderId = Guid.NewGuid();
        var outbox = Substitute.For<IOutboxWriter>();
        var kafkaOptions = Options.Create(new KafkaOptions());

        await using var seedCtx = CreateContext(dbName);
        var firstCommand = ValidCommand(orderId: orderId);
        await new ShipmentCreationHandler(seedCtx, outbox, kafkaOptions).HandleAsync(firstCommand, CancellationToken.None);

        await using var ctx = CreateContext(dbName);
        var secondCommand = firstCommand with { MessageId = Guid.NewGuid() };
        await new ShipmentCreationHandler(ctx, outbox, kafkaOptions).HandleAsync(secondCommand, CancellationToken.None);

        Assert.Equal(1, await ctx.Shipments.CountAsync());
        Assert.Equal(2, await ctx.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_WithoutRecipientName_UsesBuyerIdAsName()
    {
        var dbName = Guid.NewGuid().ToString();
        var buyerId = Guid.NewGuid();
        await using var ctx = CreateContext(dbName);
        var outbox = Substitute.For<IOutboxWriter>();
        var handler = new ShipmentCreationHandler(ctx, outbox, Options.Create(new KafkaOptions()));
        var command = ValidCommand() with
        {
            BuyerId = buyerId,
            Destination = new ShipmentAddressDto("Rua A", "1", "SP", "SP", "01310-100", "BR")
        };

        await handler.HandleAsync(command, CancellationToken.None);

        var shipment = await ctx.Shipments.FirstAsync();
        Assert.Equal(buyerId.ToString(), shipment.Destination.RecipientName);
    }

    [Fact]
    public async Task HandleAsync_PackageWithZeroSequence_AssignsIndexBasedSequence()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var outbox = Substitute.For<IOutboxWriter>();
        var handler = new ShipmentCreationHandler(ctx, outbox, Options.Create(new KafkaOptions()));
        var command = ValidCommand() with
        {
            Packages = [
                new CreateShipmentPackageDto(1m, 10m, 10m, 10m, [new CreateShipmentPackageItemDto(Guid.NewGuid(), 1)], null, 0),
                new CreateShipmentPackageDto(2m, 20m, 20m, 20m, [new CreateShipmentPackageItemDto(Guid.NewGuid(), 2)], null, 0)
            ]
        };

        await handler.HandleAsync(command, CancellationToken.None);

        var shipment = await ctx.Shipments.Include(x => x.Packages).FirstAsync();
        Assert.Contains(shipment.Packages, p => p.Sequence == 1);
        Assert.Contains(shipment.Packages, p => p.Sequence == 2);
    }
}
