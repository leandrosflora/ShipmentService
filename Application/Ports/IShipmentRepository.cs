namespace ShipmentService.Application.Ports;

public interface IShipmentRepository
{
    Task<IReadOnlyList<Guid>> ClaimBookableAsync(int limit, CancellationToken cancellationToken);
}
