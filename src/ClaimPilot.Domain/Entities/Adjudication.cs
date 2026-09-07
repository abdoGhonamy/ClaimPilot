using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Domain.Entities;

public class AdjudicationRun
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public required Claim Claim { get; set; }
    public Guid? PolicyVersionId { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public string? CorrelationId { get; set; }
    public int Iteration { get; set; }
    public int MaxIterations { get; set; }
    public string? FailReason { get; set; }
    public bool Degraded { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<AgentRun> AgentRuns { get; set; } = new List<AgentRun>();
    public ICollection<ApprovalItem> ApprovalItems { get; set; } = new List<ApprovalItem>();
    public ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();
    public ICollection<Decision> Decisions { get; set; } = new List<Decision>();
    public Decision? FinalDecision { get; set; }
    public Guid? FinalDecisionId { get; set; }
}

public class AgentRun
{
    public Guid Id { get; set; }
    public Guid AdjudicationRunId { get; set; }
    public required AdjudicationRun AdjudicationRun { get; set; }
    public AgentType AgentType { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public int Iteration { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}

public class Anomaly
{
    public Guid Id { get; set; }
    public Guid AdjudicationRunId { get; set; }
    public required AdjudicationRun AdjudicationRun { get; set; }
    public required string Type { get; set; }
    public AnomalySeverity Severity { get; set; }
    public required string Description { get; set; }
    public string? Evidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Decision
{
    public Guid Id { get; set; }
    public Guid AdjudicationRunId { get; set; }
    public required AdjudicationRun AdjudicationRun { get; set; }
    public DecisionType DecisionType { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string? Rationale { get; set; }
    public string? CitationsJson { get; set; }
    public bool IsFinal { get; set; }
    public Guid? ApprovalItemId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DecisionLetter? Letter { get; set; }
}

public class DecisionLetter
{
    public Guid Id { get; set; }
    public Guid DecisionId { get; set; }
    public required Decision Decision { get; set; }
    public required string LetterText { get; set; }
    public Guid IssuedBy { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
}