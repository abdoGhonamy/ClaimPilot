namespace ClaimPilot.Domain.Events;

public abstract record DomainEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

public sealed record ClaimSubmittedEvent(
    Guid ClaimId,
    string ClaimNumber,
    string PolicyNumber,
    DateTime IncidentDate,
    decimal ClaimAmount) : DomainEvent;

public sealed record ClaimRunStartedEvent(Guid RunId, Guid ClaimId, string? CorrelationId) : DomainEvent;

public sealed record PolicyVersionSelectedEvent(Guid RunId, Guid PolicyId, Guid VersionId, int Version,
    DateTime EffectiveDate, DateTime IncidentDate) : DomainEvent;

public sealed record DeterministicComputationEvent(Guid RunId, decimal ClaimAmount, decimal Deductible,
    decimal Coinsurance, decimal Cap, decimal Payable) : DomainEvent;

public sealed record ApprovalItemCreatedEvent(Guid ApprovalItemId, Guid RunId, decimal? ProposedAmount) : DomainEvent;

public sealed record DecisionApprovedEvent(Guid RunId, Guid ApprovalItemId, decimal ApprovedAmount) : DomainEvent;

public sealed record AuditEvent(string EntityType, Guid? EntityId, string Action, string? ActorId,
    string? Before, string? After, string? CorrelationId, string? RunId) : DomainEvent;