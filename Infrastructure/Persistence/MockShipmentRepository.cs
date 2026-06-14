using Microsoft.Extensions.Options;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.FeatureFlags;

namespace ShipmentService.Infrastructure.Persistence;

public sealed class MockShipmentRepository : IShipmentRepository
{
    private readonly ILogger<MockShipmentRepository> _logger;
    private readonly ShipmentFeatureFlags _featureFlags;

    public MockShipmentRepository(ILogger<MockShipmentRepository> logger, IOptions<ShipmentFeatureFlags> featureFlags)
    {
        _logger = logger;
        _featureFlags = featureFlags.Value;
    }

    public Task<IReadOnlyList<Guid>> ClaimBookableAsync(int limit, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Using mocked shipment repository response for {RepositoryMethod}. Feature flag {FeatureFlag} is enabled. Requested limit: {Limit}",
            nameof(ClaimBookableAsync),
            nameof(ShipmentFeatureFlags.UseMockShipmentRepository),
            limit);

        return Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
