using ClaimPilot.Application.Interfaces.Documents;

namespace ClaimPilot.Application.Interfaces.Documents;

public sealed record StoredFile(string RelativePath, long SizeBytes);

public interface IStorageService
{
    Task<StoredFile> SaveAsync(Guid claimId, Guid documentId, string extension, Stream content, CancellationToken ct);
}
