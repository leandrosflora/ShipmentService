namespace ShipmentService.Contracts;

public sealed record ShipmentCreatedIntegrationEvent(
    Guid ShipmentId,
    Guid OrderId,
    Guid BuyerId,
    string CarrierCode,
    string ServiceLevelCode,
    string ExternalShipmentId,
    string TrackingCode,
    string LabelObjectKey,
    DateOnly EstimatedDeliveryDate,
    DateTimeOffset CreatedAt);

public sealed record ShipmentCreationFailedIntegrationEvent(
    Guid MessageId,
    Guid ShipmentId,
    Guid OrderId,
    string CarrierCode,
    string Reason,
    DateTimeOffset OccurredAt);
