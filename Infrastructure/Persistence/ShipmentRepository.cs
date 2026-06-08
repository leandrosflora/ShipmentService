using Microsoft.EntityFrameworkCore;
using ShipmentService.Application.Ports;

namespace ShipmentService.Infrastructure.Persistence;

public sealed class ShipmentRepository : IShipmentRepository
{
    private readonly ShipmentDbContext _dbContext;

    public ShipmentRepository(ShipmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Guid>> ClaimBookableAsync(int limit, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE shipments
            SET status = 'BookingInProgress',
                processing_token = {token},
                processing_lease_until = {leaseUntil},
                booking_attempts = booking_attempts + 1,
                updated_at = NOW(),
                version = version + 1
            WHERE id IN
            (
                SELECT id
                FROM shipments
                WHERE
                    status = 'PendingCarrierBooking'
                    OR (
                        status = 'RetryScheduled'
                        AND next_attempt_at <= NOW()
                    )
                    OR (
                        status = 'BookingInProgress'
                        AND processing_lease_until < NOW()
                    )
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT {limit}
            )
            """, cancellationToken);

        var ids = await _dbContext.Shipments
            .Where(x => x.ProcessingToken == token)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ids;
    }
}
