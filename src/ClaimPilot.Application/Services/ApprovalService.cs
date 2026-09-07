using System.Text.Json;
using Microsoft.Extensions.Logging;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.Application.Services;

/// <summary>
/// Implements the approval queue state machine.
///
/// Valid:
///   Pending → Approved | Rejected | Edited
///   Edited  → Pending (re-review)
///   Pending → Pending (re-review / reassign / escalate)
///
/// Invalid without supervisor workflow:
///   Approved → Approved | Rejected
///   Rejected → Approved
///
/// Reject and Edit require comments. Every transition is audited.
/// </summary>
public sealed class ApprovalService : IApprovalService
{
    private readonly IApprovalRepository _approvals;
    private readonly IAuditService _audit;
    private readonly ILogger<ApprovalService> _logger;

    public ApprovalService(IApprovalRepository approvals, IAuditService audit, ILogger<ApprovalService> logger)
    {
        _approvals = approvals;
        _audit = audit;
        _logger = logger;
    }

    public async Task<ReviewActionResult> ApproveAsync(Guid id, ApproveRequest req, CancellationToken ct)
    {
        var item = await GetOrThrowAsync(id, ct);
        RequireState(item, ApprovalStatus.Pending);

        var before = State(item);
        item.Status = ApprovalStatus.Approved;
        item.ReviewedAt = DateTime.UtcNow;
        await AppendHistoryAsync(item, ApprovalAction.Approved, req.ReviewerId, req.Comment,
            before, State(item), ct);

        return new ReviewActionResult(item.Id, item.Status, item.RunId);
    }

    public async Task<ReviewActionResult> RejectAsync(Guid id, RejectRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Comment))
            throw new InvalidStateTransitionException("Rejection requires a comment.");

        var item = await GetOrThrowAsync(id, ct);
        RequireState(item, ApprovalStatus.Pending);

        var before = State(item);
        item.Status = ApprovalStatus.Rejected;
        item.ReviewedAt = DateTime.UtcNow;
        await AppendHistoryAsync(item, ApprovalAction.Rejected, req.ReviewerId, req.Comment,
            before, State(item), ct);

        return new ReviewActionResult(item.Id, item.Status, item.RunId);
    }

    public async Task<ReviewActionResult> EditAsync(Guid id, EditRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Comment))
            throw new InvalidStateTransitionException("Edit requires a comment.");
        if (string.IsNullOrWhiteSpace(req.EditedDecisionJson))
            throw new InvalidStateTransitionException("Edit requires an edited decision.");

        var item = await GetOrThrowAsync(id, ct);
        RequireState(item, ApprovalStatus.Pending);

        var before = State(item);
        var original = item.Summary;
        item.Status = ApprovalStatus.Edited;
        item.ReviewedAt = DateTime.UtcNow;
        item.Summary = req.EditedDecisionJson;

        var diff = ComputeDiff(original, req.EditedDecisionJson);
        await AppendHistoryAsync(item, ApprovalAction.Edited, req.ReviewerId, req.Comment,
            before, State(item), ct, editDiff: diff);

        return new ReviewActionResult(item.Id, ApprovalStatus.Pending, item.RunId);
    }

    public async Task<ReviewActionResult> ReReviewAsync(Guid id, ReReviewRequest req, CancellationToken ct)
    {
        var item = await GetOrThrowAsync(id, ct);

        var before = State(item);
        item.Status = ApprovalStatus.Pending;
        await AppendHistoryAsync(item, ApprovalAction.ReReview, req.ReviewerId, req.Comment,
            before, State(item), ct);

        return new ReviewActionResult(item.Id, item.Status, item.RunId);
    }

    public async Task<ReviewActionResult> AssignAsync(Guid id, AssignRequest req, CancellationToken ct)
    {
        var item = await GetOrThrowAsync(id, ct);

        var before = item.AssignedTo;
        item.AssignedTo = req.AssigneeId;
        await AppendHistoryAsync(item, ApprovalAction.Assigned, req.ReviewerId, req.Comment,
            before, item.AssignedTo, ct);

        return new ReviewActionResult(item.Id, item.Status, item.RunId);
    }

    public async Task<ReviewActionResult> EscalateAsync(Guid id, EscalateRequest req, CancellationToken ct)
    {
        var item = await GetOrThrowAsync(id, ct);

        var before = item.AssignedTo;
        item.Status = ApprovalStatus.Escalated;
        item.AssignedTo = "director";
        await AppendHistoryAsync(item, ApprovalAction.Escalated, req.ReviewerId, req.Comment,
            before, item.AssignedTo, ct);

        return new ReviewActionResult(item.Id, item.Status, item.RunId);
    }

    public async Task<ReviewActionResult> OverridePriorityAsync(Guid id, PriorityOverrideRequest req, CancellationToken ct)
    {
        var item = await GetOrThrowAsync(id, ct);

        var before = item.Priority.ToString();
        item.Priority = req.NewPriority;
        item.SLADeadline = DateTime.UtcNow.AddHours(req.NewPriority switch
        {
            Priority.Critical => 4,
            Priority.High => 8,
            _ => 24
        });
        await AppendHistoryAsync(item, ApprovalAction.PriorityOverride, req.ReviewerId, req.Comment,
            before, item.Priority.ToString(), ct);

        return new ReviewActionResult(item.Id, item.Status, item.RunId);
    }

    private async Task<ApprovalItem> GetOrThrowAsync(Guid id, CancellationToken ct)
    {
        var item = await _approvals.GetAsync(id, ct, includeHistory: false);
        return item ?? throw new InvalidStateTransitionException($"Approval item {id} not found.");
    }

    private static void RequireState(ApprovalItem item, ApprovalStatus expected)
    {
        if (item.Status != expected)
        {
            throw new InvalidStateTransitionException(
                $"Approval item {item.Id} is in state {item.Status}; {expected} required.");
        }
    }

    private static string State(ApprovalItem item) =>
        JsonSerializer.Serialize(new { item.Status, item.AssignedTo, item.Priority, item.Summary });

    private async Task AppendHistoryAsync(
        ApprovalItem item,
        ApprovalAction action,
        string? reviewerId,
        string? comment,
        string? previous,
        string? next,
        CancellationToken ct,
        string? editDiff = null)
    {
        var history = new ApprovalHistory
        {
            ApprovalItemId = item.Id,
            ApprovalItem = item,
            ReviewerId = reviewerId,
            Action = action,
            Comment = comment,
            PreviousState = previous,
            NewState = next,
            EditDiff = editDiff
        };
        await _approvals.AddHistoryAsync(history, ct);

        await _audit.RecordAsync(new AuditLogEntry(
            "ApprovalItem", item.Id, action.ToString(), actorId: reviewerId,
            before: previous, after: next, runId: item.RunId?.ToString()), ct);
    }

    private static string ComputeDiff(string? before, string after)
    {
        if (string.IsNullOrWhiteSpace(before))
            return "+ " + after;

        var beforeLines = (before ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var afterLines = after.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var diff = new List<string>();
        var common = Math.Min(beforeLines.Length, afterLines.Length);
        for (var i = 0; i < common; i++)
        {
            if (beforeLines[i] != afterLines[i])
            {
                diff.Add("- " + beforeLines[i]);
                diff.Add("+ " + afterLines[i]);
            }
        }
        for (var i = common; i < beforeLines.Length; i++) diff.Add("- " + beforeLines[i]);
        for (var i = common; i < afterLines.Length; i++) diff.Add("+ " + afterLines[i]);

        return diff.Count == 0 ? "(no textual change)" : string.Join('\n', diff);
    }
}