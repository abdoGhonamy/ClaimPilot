using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Application.Interfaces.Trace;

/// <summary>A generic trace segment persisted for later inspection by RunId.</summary>
public sealed record TraceRecord(
    string RunId,
    string EntityType,
    string? EntityId,
    string Action,
    string? ActorId,
    string? Before,
    string? After,
    string? CorrelationId,
    DateTime TimestampUtc,
    string? Message = null);

/// <summary>Append-only trace writer. Everything in a workflow is persisted.</summary>
public interface ITraceService
{
    Task WriteAsync(
        string runId,
        string entityType,
        string action,
        string? entityId = null,
        string? actorId = null,
        string? before = null,
        string? after = null,
        string? correlationId = null,
        string? message = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<TraceRecord>> GetRunAsync(string runId, CancellationToken ct);
}

/// <summary>The full trace view for a run.</summary>
public sealed record RunTraceView(
    string RunId,
    string ClaimNumber,
    string PolicyNumber,
    int? PolicyVersion,
    DateTime? EffectiveDate,
    string Status,
    bool Degraded,
    string? FailReason,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<string> RetrievedChunks,
    IReadOnlyList<string> ComputationSteps,
    IReadOnlyList<string> Anomalies,
    IReadOnlyList<UsageRecord> Usage,
    decimal? EstimatedCost,
    IReadOnlyList<string> ApprovalHistory,
    string? FinalResult);

public interface IRunTraceViewBuilder
{
    Task<RunTraceView> BuildAsync(string runId, CancellationToken ct);
}