using ShipmentService.Domain;

namespace ShipmentService.UnitTest.Domain;

public class ShipmentTests
{
    static ShipmentAddress Address() =>
        new("João Silva", "Rua das Flores", "100", null, "Centro", "São Paulo", "SP", "01310-100", "BR", null);

    static ShipmentPackage Package() =>
        new(1, 1.5m, 20m, 15m, 10m, [new ShipmentPackageItem(Guid.NewGuid(), 1)]);

    static Shipment NewShipment() =>
        Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "promise-1", "route-1", "CORREIOS", "PAC",
            Guid.NewGuid(), Address(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), [Package()]);

    // === Create ===

    [Fact]
    public void Create_ValidArgs_InitializesPendingShipment()
    {
        var s = NewShipment();

        Assert.NotEqual(Guid.Empty, s.Id);
        Assert.Equal(ShipmentStatus.PendingCarrierBooking, s.Status);
        Assert.Equal(1L, s.Version);
        Assert.Equal(0, s.BookingAttempts);
        Assert.Single(s.Packages);
    }

    [Fact]
    public void Create_EmptyShipmentRequestId_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "p", "r", "CORREIOS", "PAC", Guid.NewGuid(), Address(),
                DateOnly.FromDateTime(DateTime.Today), [Package()]));

    [Fact]
    public void Create_EmptyOrderId_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(),
                "p", "r", "CORREIOS", "PAC", Guid.NewGuid(), Address(),
                DateOnly.FromDateTime(DateTime.Today), [Package()]));

    [Fact]
    public void Create_EmptyBuyerId_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(),
                "p", "r", "CORREIOS", "PAC", Guid.NewGuid(), Address(),
                DateOnly.FromDateTime(DateTime.Today), [Package()]));

    [Fact]
    public void Create_EmptySellerId_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty,
                "p", "r", "CORREIOS", "PAC", Guid.NewGuid(), Address(),
                DateOnly.FromDateTime(DateTime.Today), [Package()]));

    [Fact]
    public void Create_EmptyOriginNodeId_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "p", "r", "CORREIOS", "PAC", Guid.Empty, Address(),
                DateOnly.FromDateTime(DateTime.Today), [Package()]));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCarrierCode_Throws(string code) =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "p", "r", code, "PAC", Guid.NewGuid(), Address(),
                DateOnly.FromDateTime(DateTime.Today), [Package()]));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankServiceLevelCode_Throws(string code) =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "p", "r", "CORREIOS", code, Guid.NewGuid(), Address(),
                DateOnly.FromDateTime(DateTime.Today), [Package()]));

    [Fact]
    public void Create_EmptyPackageList_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "p", "r", "CORREIOS", "PAC", Guid.NewGuid(), Address(),
                DateOnly.FromDateTime(DateTime.Today), []));

    // === ClaimBooking ===

    [Fact]
    public void ClaimBooking_FromPending_SetsBookingInProgress()
    {
        var s = NewShipment();
        var token = Guid.NewGuid();
        var lease = DateTimeOffset.UtcNow.AddMinutes(5);

        s.ClaimBooking(token, lease);

        Assert.Equal(ShipmentStatus.BookingInProgress, s.Status);
        Assert.Equal(token, s.ProcessingToken);
        Assert.Equal(lease, s.ProcessingLeaseUntil);
        Assert.Equal(1, s.BookingAttempts);
        Assert.Equal(2L, s.Version);
    }

    [Fact]
    public void ClaimBooking_EachRetryIncrementsAttempts()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        s.ScheduleRetry("error", DateTimeOffset.UtcNow.AddSeconds(30));

        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(2, s.BookingAttempts);
    }

    [Fact]
    public void ClaimBooking_FromReadyToShip_Throws()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        s.MarkReady("EXT", "TRACK", "key", "sha");

        Assert.Throws<InvalidOperationException>(() =>
            s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public void ClaimBooking_FromFailed_Throws()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        s.MarkFailed("reason");

        Assert.Throws<InvalidOperationException>(() =>
            s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public void ClaimBooking_FromCancelled_Throws()
    {
        var s = NewShipment();
        s.MarkCancelled();

        Assert.Throws<InvalidOperationException>(() =>
            s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    // === MarkReady ===

    [Fact]
    public void MarkReady_FromBookingInProgress_SetsReadyToShip()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));

        s.MarkReady("EXT-001", "TRACK-001", "labels/a.pdf", "sha256hash");

        Assert.Equal(ShipmentStatus.ReadyToShip, s.Status);
        Assert.Equal("EXT-001", s.ExternalShipmentId);
        Assert.Equal("TRACK-001", s.TrackingCode);
        Assert.Null(s.ProcessingToken);
        Assert.Null(s.ProcessingLeaseUntil);
        Assert.NotNull(s.ReadyAt);
    }

    [Fact]
    public void MarkReady_AlreadyReadyToShip_IsIdempotent()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        s.MarkReady("EXT-001", "TRACK-001", "labels/a.pdf", "sha256");
        var versionBefore = s.Version;

        s.MarkReady("EXT-002", "TRACK-002", "labels/b.pdf", "sha256b");

        Assert.Equal(versionBefore, s.Version);
        Assert.Equal("EXT-001", s.ExternalShipmentId);
    }

    [Fact]
    public void MarkReady_FromPendingCarrierBooking_Throws()
    {
        var s = NewShipment();

        Assert.Throws<InvalidOperationException>(() => s.MarkReady("e", "t", "k", "s"));
    }

    // === ScheduleRetry ===

    [Fact]
    public void ScheduleRetry_FromBookingInProgress_SetsRetryScheduled()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        var nextAttempt = DateTimeOffset.UtcNow.AddMinutes(2);

        s.ScheduleRetry("timeout", nextAttempt);

        Assert.Equal(ShipmentStatus.RetryScheduled, s.Status);
        Assert.Equal("timeout", s.LastError);
        Assert.Equal(nextAttempt, s.NextAttemptAt);
        Assert.Null(s.ProcessingToken);
        Assert.Null(s.ProcessingLeaseUntil);
    }

    [Fact]
    public void ScheduleRetry_TruncatesErrorTo1000Chars()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));

        s.ScheduleRetry(new string('x', 2000), DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(1000, s.LastError!.Length);
    }

    [Fact]
    public void ScheduleRetry_NotBookingInProgress_IsNoOp()
    {
        var s = NewShipment();
        var versionBefore = s.Version;

        s.ScheduleRetry("error", DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(ShipmentStatus.PendingCarrierBooking, s.Status);
        Assert.Equal(versionBefore, s.Version);
    }

    // === MarkFailed ===

    [Fact]
    public void MarkFailed_SetsFailedAndClearsProcessingInfo()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));

        s.MarkFailed("permanent error");

        Assert.Equal(ShipmentStatus.Failed, s.Status);
        Assert.Equal("permanent error", s.LastError);
        Assert.Null(s.ProcessingToken);
        Assert.Null(s.ProcessingLeaseUntil);
    }

    [Fact]
    public void MarkFailed_TruncatesErrorTo1000Chars()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));

        s.MarkFailed(new string('e', 2000));

        Assert.Equal(1000, s.LastError!.Length);
    }

    // === RequestCancellation ===

    [Fact]
    public void RequestCancellation_FromPending_SetsCancellationRequested()
    {
        var s = NewShipment();

        s.RequestCancellation();

        Assert.Equal(ShipmentStatus.CancellationRequested, s.Status);
    }

    [Fact]
    public void RequestCancellation_FromReadyToShip_SetsCancellationRequested()
    {
        var s = NewShipment();
        s.ClaimBooking(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        s.MarkReady("EXT", "TRACK", "key", "sha");

        s.RequestCancellation();

        Assert.Equal(ShipmentStatus.CancellationRequested, s.Status);
    }

    [Fact]
    public void RequestCancellation_AlreadyCancelled_IsIdempotent()
    {
        var s = NewShipment();
        s.MarkCancelled();
        var versionAfter = s.Version;

        s.RequestCancellation();

        Assert.Equal(versionAfter, s.Version);
        Assert.Equal(ShipmentStatus.Cancelled, s.Status);
    }

    [Fact]
    public void RequestCancellation_FromHandedOver_Throws()
    {
        var s = NewShipment();
        SetStatus(s, ShipmentStatus.HandedOver);

        Assert.Throws<InvalidOperationException>(() => s.RequestCancellation());
    }

    [Fact]
    public void RequestCancellation_FromInTransit_Throws()
    {
        var s = NewShipment();
        SetStatus(s, ShipmentStatus.InTransit);

        Assert.Throws<InvalidOperationException>(() => s.RequestCancellation());
    }

    [Fact]
    public void RequestCancellation_FromDelivered_Throws()
    {
        var s = NewShipment();
        SetStatus(s, ShipmentStatus.Delivered);

        Assert.Throws<InvalidOperationException>(() => s.RequestCancellation());
    }

    // === MarkCancelled ===

    [Fact]
    public void MarkCancelled_SetsCancelledStatusAndTimestamp()
    {
        var s = NewShipment();

        s.MarkCancelled();

        Assert.Equal(ShipmentStatus.Cancelled, s.Status);
        Assert.NotNull(s.CancelledAt);
    }

    [Fact]
    public void MarkCancelled_IncreasesVersion()
    {
        var s = NewShipment();
        var versionBefore = s.Version;

        s.MarkCancelled();

        Assert.Equal(versionBefore + 1, s.Version);
    }

    private static void SetStatus(Shipment shipment, ShipmentStatus status) =>
        typeof(Shipment).GetProperty(nameof(Shipment.Status))!.SetValue(shipment, status);
}
