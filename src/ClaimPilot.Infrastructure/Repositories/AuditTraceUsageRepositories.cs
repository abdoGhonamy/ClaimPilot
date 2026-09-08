using Microsoft.EntityFrameworkCore;

using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Entities;

using ClaimPilot.Infrastructure.Data;

namespace ClaimPilot.Infrastructure.Repositories;

public sealed class AuditRepository : IAuditRepository
{
    private readonly Data.AppDbContext _db;

    public AuditRepository(Data.AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(AuditLog entry, CancellationToken ct)
    {
        _db.AuditLogs.Add(entry);
        return _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLog>> QueryAsync(
        string? entityType, Guid? entityId, string? runId, int take, CancellationToken ct)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (entityType is not null) query = query.Where(a => a.EntityType == entityType);
        if (entityId.HasValue) query = query.Where(a => a.EntityId == entityId);
        if (runId is not null) query = query.Where(a => a.RunId == runId);
        return await query.OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync(ct);
    }
}

public sealed class TraceRepository : ITraceRepository
{
    private readonly Data.AppDbContext _db;

    public TraceRepository(Data.AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(TraceEntry entry, CancellationToken ct)
    {
        _db.TraceRecords.Add(new TraceRecordEntity
        {
            RunId = entry.RunId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            ActorId = entry.ActorId,
            Before = entry.Before,
            After = entry.After,
            CorrelationId = entry.CorrelationId,
            TimestampUtc = entry.TimestampUtc,
            Message = entry.Message
        });
        return _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TraceEntry>> GetByRunAsync(string runId, CancellationToken ct)
        => (await _db.TraceRecords
            .AsNoTracking()
            .Where(t => t.RunId == runId)
            .OrderBy(t => t.TimestampUtc)
            .ToListAsync(ct))
            .Select(t => new TraceEntry(t.RunId, t.EntityType, t.EntityId, t.Action, t.ActorId,
                t.Before, t.After, t.CorrelationId, t.TimestampUtc, t.Message))
            .ToList();
}

public sealed class UsageRepository : IUsageRepository
{
    private readonly Data.AppDbContext _db;

    public UsageRepository(Data.AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(UsageRecordEntry entry, CancellationToken ct)
    {
        _db.UsageRecords.Add(new UsageRecordEntity
        {
            Scope = entry.Scope,
            Provider = entry.Provider,
            Model = entry.Model,
            InputTokens = entry.InputTokens,
            OutputTokens = entry.OutputTokens,
            TotalTokens = entry.TotalTokens,
            EstimatedCostUsd = entry.EstimatedCostUsd,
            TimestampUtc = entry.TimestampUtc,
            RunId = entry.RunId,
            CorrelationId = entry.CorrelationId
        });
        return _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UsageRecordEntry>> GetByRunAsync(string runId, CancellationToken ct)
        => (await _db.UsageRecords
            .AsNoTracking()
            .Where(u => u.RunId == runId)
            .OrderBy(u => u.TimestampUtc)
            .ToListAsync(ct))
            .Select(u => new UsageRecordEntry(u.Scope, u.Provider, u.Model, u.InputTokens, u.OutputTokens,
                u.TotalTokens, u.EstimatedCostUsd, u.TimestampUtc, u.RunId, u.CorrelationId))
            .ToList();

    public async Task<decimal> SumRunCostAsync(string runId, CancellationToken ct)
    {
        var sum = await _db.UsageRecords
            .Where(u => u.RunId == runId)
            .SumAsync(u => (decimal?)u.EstimatedCostUsd, ct);
        return sum ?? 0m;
    }
}