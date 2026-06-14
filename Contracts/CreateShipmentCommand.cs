using System.Text.Json.Serialization;

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
    string Street,
    string Number,
    string City,
    string State,
    [property: JsonPropertyName("zipCode")] string PostalCode,
    string Country,
    string? RecipientName = null,
    string? Complement = null,
    string? District = null,
    string? Phone = null);

public sealed record CreateShipmentPackageDto(
    decimal WeightKg,
    decimal HeightCm,
    decimal WidthCm,
    decimal LengthCm,
    IReadOnlyList<CreateShipmentPackageItemDto> Items,
    string? PackageId = null,
    int Sequence = 0);

public sealed record CreateShipmentPackageItemDto(Guid SkuId, int Quantity);
