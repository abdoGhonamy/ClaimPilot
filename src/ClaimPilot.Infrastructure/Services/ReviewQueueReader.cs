using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>Reads the review queue surfaces for adjusters/supervisors.</summary>
internal sealed class ReviewQueueReader : IApprovalQueueReader
{
    private readonly IApprovalRepository _approvals;
    private readonly IClaimRepository _claims;

    public ReviewQueueReader(IApprovalRepository approvals, IClaimRepository claims)
    {
        _approvals = approvals;
        _claims = claims;
    }

    public async Task<IReadOnlyList<ApprovalItemView>> GetQueueAsync(ReviewQueueFilter filter, CancellationToken ct)
    {
        var items = await _approvals.QueryAsync(filter.Status, filter.AssigneeId, filter.Priority, ct);
        var views = new List<ApprovalItemView>();
        foreach (var item in items)
        {
            var claim = await _claims.GetByIdAsync(item.ClaimId, ct);
            var now = DateTime.UtcNow;
            views.Add(new ApprovalItemView(
                item.Id,
                item.Title,
                item.Summary,
                claim?.ClaimNumber ?? string.Empty,
                item.Status,
                item.Priority,
                item.SLADeadline,
                item.AssignedTo,
                IsLate: item.SLADeadline.HasValue && item.Status == ApprovalStatus.Pending && now > item.SLADeadline,
                item.CreatedAt,
                item.ReviewedAt,
                item.RunId));
        }
        return views;
    }

    public async Task<ApprovalItemDetail?> GetAsync(Guid approvalItemId, CancellationToken ct)
    {
        var item = await _approvals.GetAsync(approvalItemId, ct, includeHistory: true);
        if (item is null) return null;

        var claim = await _claims.GetByIdAsync(item.ClaimId, ct);

        // The draft summary carries the JSON draft (original and any edits kept in history).
        return new ApprovalItemDetail(
            item.Id,
            item.Title,
            item.Summary,
            item.ClaimId,
            claim?.ClaimNumber ?? string.Empty,
            item.Status,
            item.Priority,
            item.SLADeadline,
            item.AssignedTo,
            OriginalDraftJson: item.History.FirstOrDefault(h => h.Action == ApprovalAction.Created)?.NewState ?? item.Summary,
            CurrentDraftJson: item.Status == ApprovalStatus.Edited ? item.Summary : item.Summary,
            EditedDraftJson: item.History.LastOrDefault(h => h.Action == ApprovalAction.Edited)?.NewState ?? string.Empty,
            ProposedAmount: ExtractAmount(item.Summary),
            item.CreatedAt,
            item.ReviewedAt,
            item.History.OrderBy(h => h.CreatedAt).Select(h => new ApprovalHistoryView(
                h.Id, h.Action, h.ReviewerId, h.Comment, h.PreviousState, h.NewState, h.EditDiff, h.CreatedAt)).ToList());
    }

    private static decimal? ExtractAmount(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary) || !summary.StartsWith('{')) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(summary);
            if (doc.RootElement.TryGetProperty("proposed_amount", out var amount) &&
                amount.ValueKind == System.Text.Json.JsonValueKind.Number)
                return amount.GetDecimal();
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }
}