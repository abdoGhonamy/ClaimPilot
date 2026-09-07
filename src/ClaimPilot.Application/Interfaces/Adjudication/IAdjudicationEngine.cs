using ClaimPilot.Domain.ValueObjects;

namespace ClaimPilot.Application.Interfaces.Adjudication;

/// <summary>
/// Inputs to the deterministic adjudication engine. Every value is structured
/// (extracted by an agent or read from coverage tables) — never computed by an LLM.
/// </summary>
public sealed record ComputationRequest
{
    public required decimal ClaimAmount { get; init; }
    public decimal? Deductible { get; init; }
    public decimal? CoinsuranceRate { get; init; }
    public decimal? CoverageLimit { get; init; }
    public IReadOnlyList<string> ApplicableExclusions { get; init; } = Array.Empty<string>();
    public bool HasApplicableExclusions => ApplicableExclusions.Count > 0;
}

/// <summary>
/// Deterministic, pure, repeatable calculation of the payable amount.
/// Same inputs must always yield the same outputs.
/// </summary>
public interface IAdjudicationEngine
{
    Computation Compute(ComputationRequest request);
}