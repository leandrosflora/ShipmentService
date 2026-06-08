namespace ShipmentService.Domain;

public sealed class Shipment
{
    public Guid Id { get; private set; }
    public Guid ShipmentRequestId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid SellerId { get; private set; }
    public string ShippingPromiseId { get; private set; } = default!;
    public string RouteId { get; private set; } = default!;
    public string CarrierCode { get; private set; } = default!;
    public string ServiceLevelCode { get; private set; } = default!;
    public Guid OriginNodeId { get; private set; }
    public ShipmentAddress Destination { get; private set; } = default!;
    public List<ShipmentPackage> Packages { get; private set; } = [];
    public DateOnly PromisedDeliveryDate { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public string? ExternalShipmentId { get; private set; }
    public string? TrackingCode { get; private set; }
    public string? LabelObjectKey { get; private set; }
    public string? LabelSha256 { get; private set; }
    public int BookingAttempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public Guid? ProcessingToken { get; private set; }
    public DateTimeOffset? ProcessingLeaseUntil { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    private Shipment() { }

    public static Shipment Create(Guid shipmentRequestId, Guid orderId, Guid buyerId, Guid sellerId, string shippingPromiseId, string routeId, string carrierCode, string serviceLevelCode, Guid originNodeId, ShipmentAddress destination, DateOnly promisedDeliveryDate, IEnumerable<ShipmentPackage> packages)
    {
        var packageList = packages.ToList();
        if (shipmentRequestId == Guid.Empty) throw new ArgumentException("ShipmentRequestId is required");
        if (orderId == Guid.Empty) throw new ArgumentException("OrderId is required");
        if (buyerId == Guid.Empty) throw new ArgumentException("BuyerId is required");
        if (sellerId == Guid.Empty) throw new ArgumentException("SellerId is required");
        if (originNodeId == Guid.Empty) throw new ArgumentException("OriginNodeId is required");
        if (string.IsNullOrWhiteSpace(carrierCode)) throw new ArgumentException("CarrierCode is required");
        if (string.IsNullOrWhiteSpace(serviceLevelCode)) throw new ArgumentException("ServiceLevelCode is required");
        if (packageList.Count == 0) throw new ArgumentException("Shipment must have packages");

        var now = DateTimeOffset.UtcNow;
        return new Shipment
        {
            Id = Guid.NewGuid(),
            ShipmentRequestId = shipmentRequestId,
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            ShippingPromiseId = shippingPromiseId,
            RouteId = routeId,
            CarrierCode = carrierCode,
            ServiceLevelCode = serviceLevelCode,
            OriginNodeId = originNodeId,
            Destination = destination,
            PromisedDeliveryDate = promisedDeliveryDate,
            Packages = packageList,
            Status = ShipmentStatus.PendingCarrierBooking,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void ClaimBooking(Guid processingToken, DateTimeOffset leaseUntil)
    {
        if (Status is not (ShipmentStatus.PendingCarrierBooking or ShipmentStatus.RetryScheduled or ShipmentStatus.BookingInProgress)) throw new InvalidOperationException("Shipment cannot be booked in its current state");
        Status = ShipmentStatus.BookingInProgress;
        ProcessingToken = processingToken;
        ProcessingLeaseUntil = leaseUntil;
        BookingAttempts++;
        Touch();
    }

    public void MarkReady(string externalShipmentId, string trackingCode, string labelObjectKey, string labelSha256)
    {
        if (Status == ShipmentStatus.ReadyToShip) return;
        if (Status != ShipmentStatus.BookingInProgress) throw new InvalidOperationException("Shipment is not being booked");
        ExternalShipmentId = externalShipmentId;
        TrackingCode = trackingCode;
        LabelObjectKey = labelObjectKey;
        LabelSha256 = labelSha256;
        Status = ShipmentStatus.ReadyToShip;
        ProcessingToken = null;
        ProcessingLeaseUntil = null;
        NextAttemptAt = null;
        LastError = null;
        ReadyAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void ScheduleRetry(string error, DateTimeOffset nextAttemptAt)
    {
        if (Status != ShipmentStatus.BookingInProgress) return;
        Status = ShipmentStatus.RetryScheduled;
        LastError = Limit(error, 1000);
        NextAttemptAt = nextAttemptAt;
        ProcessingToken = null;
        ProcessingLeaseUntil = null;
        Touch();
    }

    public void MarkFailed(string error)
    {
        Status = ShipmentStatus.Failed;
        LastError = Limit(error, 1000);
        ProcessingToken = null;
        ProcessingLeaseUntil = null;
        Touch();
    }

    public void RequestCancellation()
    {
        if (Status == ShipmentStatus.Cancelled) return;
        if (Status is ShipmentStatus.HandedOver or ShipmentStatus.InTransit or ShipmentStatus.Delivered) throw new InvalidOperationException("Shipment can no longer be cancelled");
        Status = ShipmentStatus.CancellationRequested;
        Touch();
    }

    public void MarkCancelled()
    {
        Status = ShipmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        Touch();
    }

    private void Touch()
    {
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
