namespace ShipmentService.Contracts;

public sealed record OrderCreatedIntegrationEvent(
    Guid OrderId,
    Guid CheckoutId,
    Guid BuyerId,
    Guid SellerId,
    string ShippingPromiseId,
    string RouteId,
    string CarrierCode,
    string ServiceLevelCode,
    Guid OriginNodeId,
    DateOnly PromisedDeliveryDate,
    ShipmentAddressDto Destination,
    IReadOnlyList<CreateShipmentPackageDto> Packages,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt);
