using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Assignment;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Services;

public sealed class AssignmentOptions
{
    public AssignmentStrategy Strategy { get; set; } = AssignmentStrategy.RoundRobin;

    /// <summary>policy-key -> adjuster id for policy-based assignment.</summary>
    public Dictionary<string, string> PolicyAssignments { get; set; } = new();
}

/// <summary>
/// Strategy-driven assignment. The mechanism (which adjuster gets an item) is
/// configuration/data, not hardcoded branching. Round-robin uses the number of
/// items currently in a user's queue to pick the least-loaded adjuster.
/// </summary>
public sealed class AssignmentService : IAssignmentService
{
    private readonly AssignmentOptions _options;
    private readonly IApprovalRepository _approvals;
    private readonly IAuditService _audit;
    private readonly ILogger<AssignmentService> _logger;
    private static readonly string[] KnownAdjusters = { "adjuster", "supervisor" };

    public AssignmentService(
        IOptions<AssignmentOptions> options,
        IApprovalRepository approvals,
        IAuditService audit,
        ILogger<AssignmentService> logger)
    {
        _options = options.Value;
        _approvals = approvals;
        _audit = audit;
        _logger = logger;
    }

    public async Task<AssignmentResult> AssignAsync(Guid approvalItemId, string? preferredPolicyKey, CancellationToken ct)
    {
        string? assignee;
        string reason;

        switch (_options.Strategy)
        {
            case AssignmentStrategy.PolicyBased when preferredPolicyKey is not null &&
                                                   _options.PolicyAssignments.TryGetValue(preferredPolicyKey, out var byPolicy):
                assignee = byPolicy;
                reason = $"policy-based assignment for {preferredPolicyKey}";
                break;

            case AssignmentStrategy.PriorityBased:
            {
                var item = await _approvals.GetAsync(approvalItemId, ct);
                assignee = item is { Priority: Priority.High or Priority.Critical }
                    ? "supervisor"
                    : await LeastLoadedAsync(KnownAdjusters, ct);
                reason = assignee == "supervisor" ? "priority-based: high/critical routed to supervisor" : "priority-based: least-loaded adjuster";
                break;
            }

            case AssignmentStrategy.PolicyBased:
                assignee = KnownAdjusters[0];
                reason = "policy-based fallback (no matching config)";
                break;

            default:
                assignee = await LeastLoadedAsync(KnownAdjusters, ct);
                reason = "round-robin: least-loaded adjuster";
                break;
        }

        await _audit.RecordAsync(new AuditLogEntry(
            "ApprovalItem", approvalItemId, "Assigned", ActorId: "system",
            Before: null, After: assignee), ct);

        _logger.LogInformation("Assigned approval item {ItemId} to {Assignee} ({Reason})",
            approvalItemId, assignee, reason);

        return new AssignmentResult(assignee, _options.Strategy, reason);
    }

    private async Task<string> LeastLoadedAsync(string[] candidates, CancellationToken ct)
    {
        var min = int.MaxValue;
        var chosen = candidates[0];
        foreach (var candidate in candidates)
        {
            var count = await _approvals.FindUserQueueCountAsync(candidate, ct);
            if (count < min)
            {
                min = count;
                chosen = candidate;
            }
        }
        return chosen;
    }
}