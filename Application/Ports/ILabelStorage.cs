namespace ShipmentService.Application.Ports;

public interface ILabelStorage
{
    Task<StoredLabel> StoreAsync(Guid shipmentId, byte[] content, string contentType, CancellationToken cancellationToken);
    Task<Uri> CreateDownloadUrlAsync(string objectKey, TimeSpan validity, CancellationToken cancellationToken);
}

public sealed record StoredLabel(string ObjectKey, string Sha256, long SizeBytes, string ContentType);
