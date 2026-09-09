using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Domain.Entities;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>Append-only audit writer backed by the audit log table.</summary>
public sealed class AuditService : IAuditService
{
    private readonly IAuditRepository _audits;

    public AuditService(IAuditRepository audits)
    {
        _audits = audits;
    }

    public Task RecordAsync(AuditLogEntry entry, CancellationToken ct)
        => _audits.AddAsync(new AuditLog
        {
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            ActorId = entry.ActorId,
            Before = entry.Before,
            After = entry.After,
            CorrelationId = entry.CorrelationId,
            RunId = entry.RunId
        }, ct);

    public Task<IReadOnlyList<AuditLog>> QueryAsync(
        string? entityType, Guid? entityId, string? runId, int take = 100, CancellationToken ct = default)
        => _audits.QueryAsync(entityType, entityId, runId, take, ct);
}

/// <summary>Append-only workflow trace writer.</summary>
public sealed class TraceService : ITraceService
{
    private readonly ITraceRepository _traces;

    public TraceService(ITraceRepository traces)
    {
        _traces = traces;
    }

    public Task WriteAsync(string runId, string entityType, string action, string? entityId = null,
        string? actorId = null, string? before = null, string? after = null,
        string? correlationId = null, string? message = null, CancellationToken ct = default)
        => _traces.AddAsync(new TraceEntry(runId, entityType, entityId, action, actorId, before, after,
            correlationId, DateTime.UtcNow, message), ct);

    public async Task<IReadOnlyList<TraceRecord>> GetRunAsync(string runId, CancellationToken ct)
    {
        var entries = await _traces.GetByRunAsync(runId, ct);
        return entries
            .Select(e => new TraceRecord(e.RunId, e.EntityType, e.EntityId, e.Action, e.ActorId,
                e.Before, e.After, e.CorrelationId, e.TimestampUtc, e.Message))
            .ToList();
    }
}

/// <summary>Usage tracking for provider-independent cost accounting.</summary>
public sealed class UsageTracker : IUsageTracker
{
    private readonly IUsageRepository _usage;

    public UsageTracker(IUsageRepository usage)
    {
        _usage = usage;
    }

    public Task RecordAsync(UsageRecord usage, CancellationToken ct)
        => _usage.AddAsync(new UsageRecordEntry(usage.Scope, usage.Provider, usage.Model,
            usage.InputTokens, usage.OutputTokens, usage.TotalTokens, usage.EstimatedCostUsd,
            usage.TimestampUtc, usage.RunId, usage.CorrelationId), ct);

    public async Task<IReadOnlyList<UsageRecord>> GetForRunAsync(string runId, CancellationToken ct)
    {
        var entries = await _usage.GetByRunAsync(runId, ct);
        return entries.Select(e => new UsageRecord(e.Scope, e.Provider, e.Model, e.InputTokens,
            e.OutputTokens, e.TotalTokens, e.EstimatedCostUsd, e.TimestampUtc, e.RunId, e.CorrelationId)).ToList();
    }

    public async Task<decimal> EstimateRunCostAsync(string runId, CancellationToken ct)
        => await _usage.SumRunCostAsync(runId, ct);
}