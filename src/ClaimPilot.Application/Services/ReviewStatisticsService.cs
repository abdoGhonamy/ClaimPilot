using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Services;

/// <summary>
/// Review statistics computed from persisted approval history:
/// average review time, SLA hit rate, resolved/approve/reject/edit counts,
/// and per-adjuster breakdowns.
/// </summary>
public sealed class ReviewStatisticsService : IReviewStatisticsService
{
    private readonly IReviewDataProvider _data;

    public ReviewStatisticsService(IReviewDataProvider data)
    {
        _data = data;
    }

    public async Task<ReviewStatistics> GetAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var items = await _data.GetApprovalItemsAsync(from, to, ct);

        var resolved = items.Where(i => i.Status is ApprovalStatus.Approved or
            ApprovalStatus.Rejected or ApprovalStatus.Edited).ToList();

        var approved = resolved.Count(i => i.Status == ApprovalStatus.Approved);
        var rejected = resolved.Count(i => i.Status == ApprovalStatus.Rejected);
        var edited = resolved.Count(i => i.Status == ApprovalStatus.Edited);

        var reviewHours = resolved
            .Where(i => i.ReviewedAt.HasValue)
            .Select(i => (i.ReviewedAt!.Value - i.CreatedAt).TotalHours)
            .ToList();

        var averageHours = reviewHours.Count == 0 ? 0 : reviewHours.Average();

        double slaHit = 0;
        if (resolved.Count > 0)
        {
            var onTime = resolved.Count(i =>
            {
                if (i.ReviewedAt is null) return false;
                if (i.SLADeadline is null) return true;
                return i.ReviewedAt <= i.SLADeadline;
            });
            slaHit = (double)onTime / resolved.Count;
        }

        var perAdjuster = resolved
            .GroupBy(i => i.AssignedTo ?? "unassigned")
            .Select(g => new PerAdjusterStatistic(
                g.Key,
                g.Count(),
                g.Count(i => i.Status == ApprovalStatus.Approved),
                g.Count(i => i.Status == ApprovalStatus.Rejected),
                g.Count(i => i.Status == ApprovalStatus.Edited),
                g.Where(i => i.ReviewedAt.HasValue)
                    .Select(i => (i.ReviewedAt!.Value - i.CreatedAt).TotalHours)
                    .DefaultIfEmpty(0)
                    .Average(),
                g.Count(i =>
                {
                    if (i.ReviewedAt is null || i.SLADeadline is null) return true;
                    return i.ReviewedAt <= i.SLADeadline;
                }) / (double)g.Count()))
            .OrderByDescending(s => s.Resolved)
            .ToList();

        return new ReviewStatistics(
            resolved.Count,
            approved,
            rejected,
            edited,
            averageHours,
            slaHit,
            perAdjuster);
    }
}

/// <summary>Data provider abstraction used by ReviewStatisticsService.</summary>
public interface IReviewDataProvider
{
    Task<IReadOnlyList<StatisticRow>> GetApprovalItemsAsync(DateTime? from, DateTime? to, CancellationToken ct);
}

/// <summary>Lightweight snapshot of an approval item for statistics.</summary>
public sealed record StatisticRow(
    ApprovalStatus Status,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    DateTime? SLADeadline,
    string? AssignedTo);