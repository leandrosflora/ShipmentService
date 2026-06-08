using System.Security.Cryptography;
using ShipmentService.Application.Ports;

namespace ShipmentService.Infrastructure.Storage;

public sealed class FileSystemLabelStorage : ILabelStorage
{
    private readonly string _directory;

    public FileSystemLabelStorage(IConfiguration configuration)
    {
        _directory = configuration["LabelStorage:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "labels");
        Directory.CreateDirectory(_directory);
    }

    public async Task<StoredLabel> StoreAsync(Guid shipmentId, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var extension = contentType == "application/pdf" ? ".pdf" : ".bin";
        var fileName = $"{shipmentId:N}-{hash[..16]}{extension}";
        var path = Path.Combine(_directory, fileName);

        await File.WriteAllBytesAsync(path, content, cancellationToken);

        return new StoredLabel(fileName, hash, content.LongLength, contentType);
    }

    public Task<Uri> CreateDownloadUrlAsync(string objectKey, TimeSpan validity, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://shipment.local/labels/{objectKey}");
        return Task.FromResult(uri);
    }
}
