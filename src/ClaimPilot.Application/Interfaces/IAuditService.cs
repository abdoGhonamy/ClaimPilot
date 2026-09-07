using ClaimPilot.Domain.Entities;

namespace ClaimPilot.Application.Interfaces;

/// <summary>Append-only audit writer. Audit records are never mutated or deleted.</summary>
public interface IAuditService
{
    Task RecordAsync(AuditLogEntry entry, CancellationToken ct);
    Task<IReadOnlyList<AuditLog>> QueryAsync(
        string? entityType = null, Guid? entityId = null, string? runId = null, int take = 100, CancellationToken ct = default);
}

public sealed record AuditLogEntry(
    string EntityType,
    Guid? EntityId,
    string Action,
    string? ActorId = null,
    string? Before = null,
    string? After = null,
    string? CorrelationId = null,
    string? RunId = null);