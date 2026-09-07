using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Interfaces.Review;

/// <summary>Review statistics: resolution times, SLA hit rates, per-adjuster breakdown.</summary>
public sealed record ReviewStatistics(
    int TotalResolved,
    int Approved,
    int Rejected,
    int Edited,
    double AverageReviewHours,
    double SlaHitRate,
    IReadOnlyList<PerAdjusterStatistic> PerAdjuster);

public sealed record PerAdjusterStatistic(
    string AdjusterId,
    int Resolved,
    int Approved,
    int Rejected,
    int Edited,
    double AverageReviewHours,
    double SlaHitRate);

public interface IReviewStatisticsService
{
    Task<ReviewStatistics> GetAsync(DateTime? from, DateTime? to, CancellationToken ct);
}