using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using ShipmentService.Application;
using ShipmentService.Application.Ports;
using ShipmentService.Domain;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.UnitTest.Application;

public class ShipmentCancellationServiceTests
{
    static ShipmentDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ShipmentDbContext(options);
    }

    static Shipment NewShipment() =>
        Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "p", "r", "CORREIOS", "PAC", Guid.NewGuid(),
            new ShipmentAddress("João", "Rua A", "1", null, "Bairro", "SP", "SP", "01310-100", "BR", null),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            [new ShipmentPackage(1, 1m, 10m, 10m, 10m, [new ShipmentPackageItem(Guid.NewGuid(), 1)])]);

    [Fact]
    public async Task RequestCancellationAsync_ExistingShipment_SetsCancellationRequested()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = NewShipment();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        await using var ctx = CreateContext(dbName);
        var service = new ShipmentCancellationService(ctx, Substitute.For<IOutboxWriter>());

        await service.RequestCancellationAsync(shipment.Id, "key-1", CancellationToken.None);

        var updated = await ctx.Shipments.FindAsync(shipment.Id);
        Assert.Equal(ShipmentStatus.CancellationRequested, updated!.Status);
    }

    [Fact]
    public async Task RequestCancellationAsync_WritesCommandToOutbox()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = NewShipment();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        await using var ctx = CreateContext(dbName);
        var outbox = Substitute.For<IOutboxWriter>();
        var service = new ShipmentCancellationService(ctx, outbox);

        await service.RequestCancellationAsync(shipment.Id, "key-1", CancellationToken.None);

        await outbox.Received(1).AddAsync(
            "carrier-shipment.commands",
            shipment.Id.ToString(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestCancellationAsync_ShipmentNotFound_ThrowsKeyNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var service = new ShipmentCancellationService(ctx, Substitute.For<IOutboxWriter>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RequestCancellationAsync(Guid.NewGuid(), "key-1", CancellationToken.None));
    }

    [Fact]
    public async Task RequestCancellationAsync_SameIdempotencyKey_DoesNotDuplicateInbox()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = NewShipment();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        await using var ctx = CreateContext(dbName);
        var outbox = Substitute.For<IOutboxWriter>();
        var service = new ShipmentCancellationService(ctx, outbox);

        await service.RequestCancellationAsync(shipment.Id, "key-1", CancellationToken.None);
        await service.RequestCancellationAsync(shipment.Id, "key-1", CancellationToken.None);

        Assert.Equal(1, await ctx.InboxMessages.CountAsync());
        await outbox.Received(1).AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestCancellationAsync_DifferentIdempotencyKeys_ProcessesBothRequests()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = NewShipment();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        await using var ctx = CreateContext(dbName);
        var outbox = Substitute.For<IOutboxWriter>();
        var service = new ShipmentCancellationService(ctx, outbox);

        await service.RequestCancellationAsync(shipment.Id, "key-1", CancellationToken.None);
        await service.RequestCancellationAsync(shipment.Id, "key-2", CancellationToken.None);

        Assert.Equal(2, await ctx.InboxMessages.CountAsync());
    }
}
