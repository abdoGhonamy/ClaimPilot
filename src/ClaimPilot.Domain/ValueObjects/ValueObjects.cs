namespace ClaimPilot.Domain.ValueObjects;

/// <summary>
/// A precise reference back to a policy chunk used as evidence.
/// No invented information may ever be attributed to a citation.
/// </summary>
public sealed record Citation
{
    public required string ChunkId { get; init; }
    public required string PolicyId { get; init; }
    public required int Version { get; init; }
    public required string Section { get; init; }
    public required string Clause { get; init; }
    public int? Page { get; init; }
    public string? TextExcerpt { get; init; }
    public string? Source { get; init; }
}

public sealed record ComputationStep
{
    public required string Step { get; init; }
    public required string Description { get; init; }
    public decimal? Amount { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Result of the deterministic adjudication engine.
/// Every amount is produced by pure arithmetic, never by an LLM.
/// </summary>
public sealed record Computation
{
    public decimal ClaimAmount { get; init; }
    public decimal? Deductible { get; init; }
    public decimal? CoinsuranceRate { get; init; }
    public decimal? CoverageLimit { get; init; }
    public decimal? CoinsuranceAmount { get; init; }
    public decimal? CoveredAmount { get; init; }
    public decimal? Cap { get; init; }
    public decimal Payable { get; init; }
    public IReadOnlyList<ComputationStep> StepTrace { get; init; } = Array.Empty<ComputationStep>();
    public string? InsufficiencyReason { get; init; }
    public bool Sufficient { get; init; }
}