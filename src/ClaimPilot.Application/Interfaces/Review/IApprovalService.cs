using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Interfaces.Review;

public sealed record ApproveRequest(string ReviewerId, string? Comment);
public sealed record RejectRequest(string ReviewerId, string Comment);
public sealed record EditRequest(string ReviewerId, string Comment, string EditedDecisionJson, decimal? EditedAmount);
public sealed record ReReviewRequest(string ReviewerId, string? Comment);
public sealed record AssignRequest(AssigneeRole Assignee, string ActorId, string? Comment = null);
public sealed record EscalateRequest(string ActorId, IReadOnlyList<string> ReviewerRoles, string? Comment = null);
public sealed record PriorityOverrideRequest(string ReviewerId, Priority NewPriority, string? Comment);

public sealed record ReviewActionResult(Guid ApprovalItemId, ApprovalStatus NewStatus, Guid? RunId);

/// <summary>
/// Human review queue workflow with a state machine:
/// Pending → Approved | Rejected | Edited; Edited → Pending (re-review).
/// Every action is audited with reviewer, comment, timestamps, and state deltas.
/// </summary>
public interface IApprovalService
{
    Task<ReviewActionResult> ApproveAsync(Guid approvalItemId, ApproveRequest request, CancellationToken ct);
    Task<ReviewActionResult> RejectAsync(Guid approvalItemId, RejectRequest request, CancellationToken ct);
    Task<ReviewActionResult> EditAsync(Guid approvalItemId, EditRequest request, CancellationToken ct);
    Task<ReviewActionResult> ReReviewAsync(Guid approvalItemId, ReReviewRequest request, CancellationToken ct);
    Task<ReviewActionResult> AssignAsync(Guid approvalItemId, AssignRequest request, CancellationToken ct);
    Task<ReviewActionResult> EscalateAsync(Guid approvalItemId, EscalateRequest request, CancellationToken ct);
    Task<ReviewActionResult> OverridePriorityAsync(Guid approvalItemId, PriorityOverrideRequest request, CancellationToken ct);
}

public sealed record ReviewQueueFilter(
    ApprovalStatus? Status = null,
    AssigneeRole? AssigneeId = null,
    string? PolicyNumber = null,
    Priority? Priority = null);

/// <summary>Snapshot of an approval item for queue rendering.</summary>
public sealed record ApprovalItemView(
    Guid Id,
    string? Title,
    string? Summary,
    string ClaimNumber,
    ApprovalStatus Status,
    Priority Priority,
    DateTime? SLADeadline,
    AssigneeRole? AssignedTo,
    bool IsLate,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    Guid? RunId);

public interface IApprovalQueueReader
{
    Task<IReadOnlyList<ApprovalItemView>> GetQueueAsync(ReviewQueueFilter filter, CancellationToken ct);
    Task<ApprovalItemDetail?> GetAsync(Guid approvalItemId, CancellationToken ct);
}

public sealed record ApprovalItemDetail(
    Guid Id,
    string? Title,
    string? Summary,
    Guid ClaimId,
    string ClaimNumber,
    ApprovalStatus Status,
    Priority Priority,
    DateTime? SLADeadline,
    AssigneeRole? AssignedTo,
    string? OriginalDraftJson,
    string? CurrentDraftJson,
    string? EditedDraftJson,
    decimal? ProposedAmount,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    IReadOnlyList<ApprovalHistoryView> History);

public sealed record ApprovalHistoryView(
    Guid Id,
    ApprovalAction Action,
    string? ReviewerId,
    string? Comment,
    string? PreviousState,
    string? NewState,
    string? EditDiff,
    DateTime CreatedAt);