using ClaimPilot.Domain.ValueObjects;

namespace ClaimPilot.Application.Interfaces.Retrieval;

/// <summary>
/// A retrieval operation is scoped to a policy version. Version filtering happens
/// BEFORE any dense/keyword retrieval ever runs. The caller pins the version.
/// </summary>
public sealed record RetrievalQuery
{
    public required Guid PolicyId { get; init; }
    public required Guid PolicyVersionId { get; init; }
    public required string Question { get; init; }
    public string? Section { get; init; }
    public string? Clause { get; init; }
    public int TopK { get; init; } = 5;
}

public sealed record RetrievalResult
{
    public required IReadOnlyList<RetrievedChunk> Chunks { get; init; }
    public bool Sufficient { get; init; }
    public string? InsufficiencyReason { get; init; }
    public required string RetrievalTraceId { get; init; }
}

public sealed record RetrievedChunk
{
    public required string ChunkId { get; init; }
    public required string PolicyId { get; init; }
    public required string PolicyNumber { get; init; }
    public required int Version { get; init; }
    public required DateTime EffectiveDate { get; init; }
    public required string Section { get; init; }
    public required string Clause { get; init; }
    public int? Page { get; init; }
    public required string Text { get; init; }
    public required Citation Citation { get; init; }
    public float Score { get; init; }
    public int DenseRank { get; init; }
    public int KeywordRank { get; init; }
}

public sealed record AskResult
{
    public required string Answer { get; set; }
    public required IReadOnlyList<RetrievedChunk> Citations { get; init; }
    public bool Refused { get; init; }
    public string? RefusalReason { get; init; }
    public string? RunId { get; init; }
}

/// <summary>
/// Hybrid retrieval combining dense vector search on pgvector with keyword
/// (full-text) search, merged by reciprocal rank fusion.
/// </summary>
public interface IRetrievalService
{
    Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct);
    Task<AskResult> AskAsync(string question, string policyNumber, DateTime? incidentDate, CancellationToken ct);
}