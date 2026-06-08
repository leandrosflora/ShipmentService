namespace ShipmentService.Contracts;

public sealed record CreateShipmentCommand(
    Guid MessageId,
    Guid ShipmentRequestId,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    string ShippingPromiseId,
    string RouteId,
    string CarrierCode,
    string ServiceLevelCode,
    Guid OriginNodeId,
    DateOnly PromisedDeliveryDate,
    ShipmentAddressDto Destination,
    IReadOnlyList<CreateShipmentPackageDto> Packages);

public sealed record ShipmentAddressDto(
    string RecipientName,
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string State,
    string PostalCode,
    string Country,
    string? Phone);

public sealed record CreateShipmentPackageDto(
    int Sequence,
    decimal WeightKg,
    decimal HeightCm,
    decimal WidthCm,
    decimal LengthCm,
    IReadOnlyList<CreateShipmentPackageItemDto> Items);

public sealed record CreateShipmentPackageItemDto(Guid SkuId, int Quantity);
