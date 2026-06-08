using ShipmentService.Contracts;

namespace ShipmentService.Application.Ports;

public interface ICarrierShipmentClient
{
    Task<CreateCarrierShipmentResponse> CreateAsync(CreateCarrierShipmentRequest request, string idempotencyKey, CancellationToken cancellationToken);
}
