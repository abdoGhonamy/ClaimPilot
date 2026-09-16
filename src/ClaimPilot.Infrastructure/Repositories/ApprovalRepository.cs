using Microsoft.EntityFrameworkCore;

using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Repositories;

public sealed class ApprovalRepository : IApprovalRepository
{
    private readonly Data.AppDbContext _db;

    public ApprovalRepository(Data.AppDbContext db)
    {
        _db = db;
    }

    public async Task<ApprovalItem?> GetAsync(Guid id, CancellationToken ct, bool includeHistory = false)
    {
        var query = _db.ApprovalItems.AsQueryable();
        if (includeHistory)
            query = query.Include(i => i.History);
        return await query.FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<ApprovalItem> AddAsync(ApprovalItem item, CancellationToken ct)
    {
        _db.ApprovalItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return item;
    }

    public Task AddHistoryAsync(ApprovalHistory history, CancellationToken ct)
    {
        _db.ApprovalHistories.Add(history);
        return _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApprovalItem>> QueryAsync(
        ApprovalStatus? status, AssigneeRole? assigneeId, Priority? priority, CancellationToken ct)
    {
        var query = _db.ApprovalItems
            .Include(i => i.History)
            .AsQueryable();
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        if (assigneeId.HasValue) query = query.Where(i => i.AssignedTo == assigneeId.Value);
        if (priority.HasValue) query = query.Where(i => i.Priority == priority.Value);
        return await query.OrderByDescending(i => i.Priority).ThenBy(i => i.CreatedAt).ToListAsync(ct);
    }

    public Task<int> FindUserQueueCountAsync(AssigneeRole? assigneeId, CancellationToken ct)
    {
        if (!assigneeId.HasValue) return Task.FromResult(0);
        return _db.ApprovalItems.CountAsync(i => i.AssignedTo == assigneeId.Value && i.Status == ApprovalStatus.Pending, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}