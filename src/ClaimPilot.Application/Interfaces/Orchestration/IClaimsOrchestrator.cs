namespace ClaimPilot.Application.Interfaces.Orchestration;

/// <summary>Observed events emitted by the orchestrator for SSE / live tracing.</summary>
public sealed record OrchestrationEvent
{
    public required string EventType { get; init; }
    public required Guid RunId { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTime OccurredAt { get; init; }
    public string? Agent { get; init; }
    public string? Tool { get; init; }
    public string? Payload { get; init; }
}

public delegate Task OrchestrationEventHandler(OrchestrationEvent @event, CancellationToken ct);

public sealed record RunResult
{
    public required Guid RunId { get; init; }
    public required Guid ClaimId { get; init; }
    public required string ClaimNumber { get; init; }
    public required string PolicyNumber { get; init; }
    public required Guid PolicyVersionId { get; init; }
    public required int PolicyVersion { get; init; }
    public required DateTime EffectiveDate { get; init; }
    public required bool ReviewRequired { get; init; }
    public Guid? ApprovalItemId { get; init; }
    public decimal? ProposedPayout { get; init; }
    public required string Status { get; init; }
    public bool Degraded { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string> AnomalySummary { get; init; } = Array.Empty<string>();
    public int IterationsUsed { get; init; }
}

/// <summary>
/// The supervisor. Coordinates agents, gates write tools, enforces an iteration
/// limit and stops once a recommendation is ready for human review.
/// </summary>
public interface IClaimsOrchestrator
{
    Task<RunResult> RunAsync(Guid claimId, string? correlationId, CancellationToken ct);
    event OrchestrationEventHandler? EventRaised;
}