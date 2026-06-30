using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShipmentService.Application;
using ShipmentService.Application.Ports;
using ShipmentService.Contracts;
using ShipmentService.Domain;
using ShipmentService.Infrastructure.Carrier;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.UnitTest.Application;

public class CarrierBookingProcessorTests
{
    static ShipmentDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ShipmentDbContext(options);
    }

    static Shipment ShipmentInBookingInProgress()
    {
        var s = Shipment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "p", "r", "CORREIOS", "PAC", Guid.NewGuid(),
            new ShipmentAddress("João", "Rua A", "1", null, "Bairro", "SP", "SP", "01310-100", "BR", null),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            [new ShipmentPackage(1, 1m, 10m, 10m, 10m, [new ShipmentPackageItem(Guid.NewGuid(), 1)])]);
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        return s;
    }

    static CreateCarrierShipmentResponse ValidCarrierResponse() => new(
        "EXT-001", "TRACK-001", "application/pdf", Convert.ToBase64String([1, 2, 3]));

    static StoredLabel ValidStoredLabel() => new("labels/shipment.pdf", "sha256abc", 3, "application/pdf");

    [Fact]
    public async Task ProcessAsync_CarrierSucceeds_MarksShipmentReadyToShip()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = ShipmentInBookingInProgress();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        var carrierClient = Substitute.For<ICarrierShipmentClient>();
        carrierClient.CreateAsync(Arg.Any<CreateCarrierShipmentRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ValidCarrierResponse()));

        var labelStorage = Substitute.For<ILabelStorage>();
        labelStorage.StoreAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ValidStoredLabel()));

        await using var ctx = CreateContext(dbName);
        var processor = new CarrierBookingProcessor(ctx, carrierClient, labelStorage, Substitute.For<IOutboxWriter>(), NullLogger<CarrierBookingProcessor>.Instance);

        await processor.ProcessAsync(shipment.Id, CancellationToken.None);

        var updated = await ctx.Shipments.FindAsync(shipment.Id);
        Assert.Equal(ShipmentStatus.ReadyToShip, updated!.Status);
        Assert.Equal("EXT-001", updated.ExternalShipmentId);
        Assert.Equal("TRACK-001", updated.TrackingCode);
        Assert.Equal("labels/shipment.pdf", updated.LabelObjectKey);
        Assert.Null(updated.ProcessingToken);
    }

    [Fact]
    public async Task ProcessAsync_CarrierSucceeds_PublishesShipmentCreatedEvent()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = ShipmentInBookingInProgress();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        var carrierClient = Substitute.For<ICarrierShipmentClient>();
        carrierClient.CreateAsync(Arg.Any<CreateCarrierShipmentRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ValidCarrierResponse()));

        var labelStorage = Substitute.For<ILabelStorage>();
        labelStorage.StoreAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ValidStoredLabel()));

        var outbox = Substitute.For<IOutboxWriter>();

        await using var ctx = CreateContext(dbName);
        var processor = new CarrierBookingProcessor(ctx, carrierClient, labelStorage, outbox, NullLogger<CarrierBookingProcessor>.Instance);

        await processor.ProcessAsync(shipment.Id, CancellationToken.None);

        await outbox.Received(1).AddAsync(
            "shipment.events",
            shipment.OrderId.ToString(),
            Arg.Any<EventEnvelope<ShipmentCreatedIntegrationEvent>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TransientCarrierError_SchedulesRetry()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = ShipmentInBookingInProgress();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        var carrierClient = Substitute.For<ICarrierShipmentClient>();
        carrierClient.CreateAsync(Arg.Any<CreateCarrierShipmentRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CreateCarrierShipmentResponse>(new HttpRequestException("Service unavailable")));

        await using var ctx = CreateContext(dbName);
        var processor = new CarrierBookingProcessor(ctx, carrierClient, Substitute.For<ILabelStorage>(), Substitute.For<IOutboxWriter>(), NullLogger<CarrierBookingProcessor>.Instance);

        await processor.ProcessAsync(shipment.Id, CancellationToken.None);

        var updated = await ctx.Shipments.FindAsync(shipment.Id);
        Assert.Equal(ShipmentStatus.RetryScheduled, updated!.Status);
        Assert.NotNull(updated.LastError);
        Assert.Contains("unavailable", updated.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(updated.NextAttemptAt);
    }

    [Fact]
    public async Task ProcessAsync_PermanentCarrierError_MarksShipmentFailed()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = ShipmentInBookingInProgress();

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        var carrierClient = Substitute.For<ICarrierShipmentClient>();
        carrierClient.CreateAsync(Arg.Any<CreateCarrierShipmentRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CreateCarrierShipmentResponse>(new PermanentCarrierException("Invalid destination address")));

        var outbox = Substitute.For<IOutboxWriter>();

        await using var ctx = CreateContext(dbName);
        var processor = new CarrierBookingProcessor(ctx, carrierClient, Substitute.For<ILabelStorage>(), outbox, NullLogger<CarrierBookingProcessor>.Instance);

        await processor.ProcessAsync(shipment.Id, CancellationToken.None);

        var updated = await ctx.Shipments.FindAsync(shipment.Id);
        Assert.Equal(ShipmentStatus.Failed, updated!.Status);
        Assert.Equal("Invalid destination address", updated.LastError);
        await outbox.Received(1).AddAsync(
            "shipment.events",
            Arg.Any<string>(),
            Arg.Any<ShipmentCreationFailedIntegrationEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_MaxAttemptsExceeded_FailsPermanently()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = ShipmentInBookingInProgress(); // BookingAttempts = 1

        // Simulate 7 more retry cycles to reach BookingAttempts = 8
        for (var i = 0; i < 7; i++)
        {
            shipment.ScheduleRetry("transient", DateTimeOffset.UtcNow.AddSeconds(1));
            shipment.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));
        }

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        var carrierClient = Substitute.For<ICarrierShipmentClient>();
        carrierClient.CreateAsync(Arg.Any<CreateCarrierShipmentRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CreateCarrierShipmentResponse>(new HttpRequestException("timeout")));

        await using var ctx = CreateContext(dbName);
        var processor = new CarrierBookingProcessor(ctx, carrierClient, Substitute.For<ILabelStorage>(), Substitute.For<IOutboxWriter>(), NullLogger<CarrierBookingProcessor>.Instance);

        await processor.ProcessAsync(shipment.Id, CancellationToken.None);

        var updated = await ctx.Shipments.FindAsync(shipment.Id);
        Assert.Equal(ShipmentStatus.Failed, updated!.Status);
        Assert.Contains("Maximum", updated.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_ShipmentNotFound_ReturnsWithoutError()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var processor = new CarrierBookingProcessor(ctx, Substitute.For<ICarrierShipmentClient>(), Substitute.For<ILabelStorage>(), Substitute.For<IOutboxWriter>(), NullLogger<CarrierBookingProcessor>.Instance);

        var exception = await Record.ExceptionAsync(() => processor.ProcessAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ProcessAsync_AlreadyReadyToShip_ReturnsWithoutCallingCarrier()
    {
        var dbName = Guid.NewGuid().ToString();
        var shipment = ShipmentInBookingInProgress();
        shipment.MarkReady("EXT", "TRACK", "key", "sha");

        await using var seedCtx = CreateContext(dbName);
        seedCtx.Shipments.Add(shipment);
        await seedCtx.SaveChangesAsync();

        var carrierClient = Substitute.For<ICarrierShipmentClient>();

        await using var ctx = CreateContext(dbName);
        var processor = new CarrierBookingProcessor(ctx, carrierClient, Substitute.For<ILabelStorage>(), Substitute.For<IOutboxWriter>(), NullLogger<CarrierBookingProcessor>.Instance);

        await processor.ProcessAsync(shipment.Id, CancellationToken.None);

        await carrierClient.DidNotReceive().CreateAsync(Arg.Any<CreateCarrierShipmentRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
