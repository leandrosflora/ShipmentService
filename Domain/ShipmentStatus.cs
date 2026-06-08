namespace ShipmentService.Domain;

public enum ShipmentStatus
{
    PendingCarrierBooking = 1,
    BookingInProgress = 2,
    RetryScheduled = 3,
    ReadyToShip = 4,
    HandedOver = 5,
    InTransit = 6,
    Delivered = 7,
    CancellationRequested = 8,
    Cancelled = 9,
    Failed = 10
}
