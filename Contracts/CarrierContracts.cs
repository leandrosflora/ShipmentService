namespace ShipmentService.Contracts;

public sealed record CreateCarrierShipmentRequest(
    Guid ShipmentId,
    string CarrierCode,
    string ServiceLevelCode,
    string RouteId,
    Guid OriginNodeId,
    ShipmentAddressDto Destination,
    IReadOnlyList<CarrierPackageDto> Packages);

public sealed record CarrierPackageDto(
    int Sequence,
    decimal WeightKg,
    decimal HeightCm,
    decimal WidthCm,
    decimal LengthCm,
    IReadOnlyList<CarrierPackageItemDto> Items);

public sealed record CarrierPackageItemDto(Guid SkuId, int Quantity);

public sealed record CreateCarrierShipmentResponse(
    string ExternalShipmentId,
    string TrackingCode,
    string LabelMimeType,
    string LabelContentBase64);
