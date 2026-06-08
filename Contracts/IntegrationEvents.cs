namespace ShipmentService.Contracts;

public sealed record ShipmentCreatedIntegrationEvent(
    Guid MessageId,
    Guid ShipmentId,
    Guid OrderId,
    string CarrierCode,
    string ServiceLevelCode,
    string ExternalShipmentId,
    string TrackingCode,
    string LabelObjectKey,
    DateOnly PromisedDeliveryDate,
    DateTimeOffset OccurredAt);

public sealed record ShipmentCreationFailedIntegrationEvent(
    Guid MessageId,
    Guid ShipmentId,
    Guid OrderId,
    string CarrierCode,
    string Reason,
    DateTimeOffset OccurredAt);
