using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Interfaces.Documents;

/// <summary>Status of an ingestion batch.</summary>
public sealed record IngestionResult
{
    public required string DocumentReference { get; init; }
    public required DocumentStatus Status { get; init; }
    public Guid? BatchId { get; init; }
    public int ChunksCreated { get; init; }
    public int ChunksSkipped { get; init; }
    public string? Error { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record IngestedChunkPayload
{
    public required string Content { get; init; }
    public required string Section { get; init; }
    public required string Clause { get; init; }
    public int? Page { get; init; }
}

/// <summary>
/// The ingestion pipeline: validate → extract → clean → detect sections/clauses →
/// structural chunks → embed → store. Idempotent via ContentHash.
/// </summary>
public interface IDocumentIngestionService
{
    Task<IngestionResult> IngestAsync(
        string policyNumber,
        int version,
        DateTime effectiveDate,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken ct);
}