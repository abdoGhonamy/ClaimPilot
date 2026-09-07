using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Interfaces;

/// <summary>Pure computation of an approval item priority.</summary>
public sealed record PriorityInput(
    decimal ClaimAmount,
    decimal? PolicyLimit,
    DateTime CreatedAt,
    DateTime IncidentDate,
    int DaysInSystem);

public interface IPriorityCalculator
{
    Priority Calculate(PriorityInput input);
}

/// <summary>Records usage events for cost accounting (provider-independent).</summary>
public interface IUsageTracker
{
    Task RecordAsync(UsageRecord usage, CancellationToken ct);
    Task<IReadOnlyList<UsageRecord>> GetForRunAsync(string runId, CancellationToken ct);
    Task<decimal> EstimateRunCostAsync(string runId, CancellationToken ct);
}