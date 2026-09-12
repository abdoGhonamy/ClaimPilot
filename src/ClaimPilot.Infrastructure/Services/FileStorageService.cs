using ClaimPilot.Application.Interfaces.Documents;
using ClaimPilot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimPilot.Infrastructure.Services;

public sealed class FileStorageService : IStorageService
{
    private readonly StorageOptions _options;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(IOptions<StorageOptions> options, ILogger<FileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredFile> SaveAsync(Guid claimId, Guid documentId, string extension, Stream content, CancellationToken ct)
    {
        var root = Path.GetFullPath(_options.UploadRoot);
        var relative = Path.Combine(claimId.ToString("N"), $"{documentId:N}{extension}");
        var destination = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var fileStream = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, useAsync: true);
        await content.CopyToAsync(fileStream, ct);
        await fileStream.FlushAsync(ct);

        var size = new FileInfo(destination).Length;
        _logger.LogInformation("Stored claim document {DocumentId} ({Bytes} bytes) for claim {ClaimId}", documentId, size, claimId);

        return new StoredFile(relative, size);
    }
}
