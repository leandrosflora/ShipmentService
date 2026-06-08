using Microsoft.EntityFrameworkCore;
using ShipmentService.Application;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.Persistence;

namespace ShipmentService.Api;

public static class ShipmentEndpoints
{
    public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shipments").WithTags("Shipments");

        group.MapGet("/{shipmentId:guid}", async (Guid shipmentId, ShipmentDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var shipment = await dbContext.Shipments
                .AsNoTracking()
                .Include(x => x.Packages).ThenInclude(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == shipmentId, cancellationToken);

            if (shipment is null) return Results.NotFound();

            return Results.Ok(new
            {
                shipment.Id,
                shipment.OrderId,
                Status = shipment.Status.ToString(),
                shipment.CarrierCode,
                shipment.ServiceLevelCode,
                shipment.TrackingCode,
                shipment.PromisedDeliveryDate,
                shipment.CreatedAt,
                shipment.ReadyAt,
                Packages = shipment.Packages.Select(x => new
                {
                    x.Sequence,
                    x.WeightKg,
                    x.HeightCm,
                    x.WidthCm,
                    x.LengthCm,
                    Items = x.Items.Select(item => new
                    {
                        item.SkuId,
                        item.Quantity
                    })
                })
            });
        });

        group.MapGet("/{shipmentId:guid}/label", async (Guid shipmentId, ShipmentDbContext dbContext, ILabelStorage storage, CancellationToken cancellationToken) =>
        {
            var labelKey = await dbContext.Shipments
                .AsNoTracking()
                .Where(x => x.Id == shipmentId)
                .Select(x => x.LabelObjectKey)
                .SingleOrDefaultAsync(cancellationToken);

            if (labelKey is null) return Results.NotFound();

            var downloadUrl = await storage.CreateDownloadUrlAsync(labelKey, TimeSpan.FromMinutes(5), cancellationToken);

            return Results.Ok(new
            {
                url = downloadUrl,
                expiresInSeconds = 300
            });
        });

        group.MapPost("/{shipmentId:guid}/cancel", async (Guid shipmentId, HttpContext context, ShipmentCancellationService service, CancellationToken cancellationToken) =>
        {
            var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.BadRequest(new { error = "Idempotency-Key is required" });
            }

            try
            {
                await service.RequestCancellationAsync(shipmentId, idempotencyKey, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }

            return Results.Accepted($"/shipments/{shipmentId}");
        });

        return app;
    }
}
