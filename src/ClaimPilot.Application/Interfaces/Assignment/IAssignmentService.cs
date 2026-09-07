using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Interfaces.Assignment;

/// <summary>Strategy-driven assignment. Policies are data, not hardcoded branching.</summary>
public enum AssignmentStrategy
{
    PolicyBased = 1,
    PriorityBased = 2,
    RoundRobin = 3
}

public sealed record AssignmentResult(string? AssigneeId, AssignmentStrategy Strategy, string Reason);

public interface IAssignmentService
{
    Task<AssignmentResult> AssignAsync(Guid approvalItemId, string? preferredPolicyKey, CancellationToken ct);
}

/// <summary>Configurable SLA rules. Defaults are overridable via configuration/database.</summary>
public sealed class SlaRuleSet
{
    public TimeSpan UnassignedAfter { get; set; } = TimeSpan.FromHours(2);
    public string UnassignedAction { get; set; } = "pool";
    public TimeSpan UnreviewedAfter { get; set; } = TimeSpan.FromHours(8);
    public string UnreviewedAction { get; set; } = "supervisor";
    public TimeSpan LateAfter { get; set; } = TimeSpan.FromHours(16);
    public string LateAction { get; set; } = "director";
}

public interface ISlaPolicy
{
    SlaRuleSet Rules { get; }
    DateTime? ComputeDeadline(DateTime created, Priority priority);
    string EvaluateEscalation(DateTime createdAt, DateTime? reviewedAt, string? assignedTo, DateTime now);
}