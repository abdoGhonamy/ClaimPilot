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
        ApprovalStatus? status, string? assigneeId, Priority? priority, CancellationToken ct)
    {
        var query = _db.ApprovalItems
            .Include(i => i.History)
            .AsQueryable();
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        if (assigneeId is not null)
        {
            if (!Enum.TryParse<AssigneeRole>(assigneeId, ignoreCase: true, out var role))
                return Array.Empty<ApprovalItem>();
            query = query.Where(i => i.AssignedTo == role);
        }
        if (priority.HasValue) query = query.Where(i => i.Priority == priority.Value);
        return await query.OrderByDescending(i => i.Priority).ThenBy(i => i.CreatedAt).ToListAsync(ct);
    }

    public async Task<int> FindUserQueueCountAsync(string assigneeId, CancellationToken ct)
    {
        if (!Enum.TryParse<AssigneeRole>(assigneeId, ignoreCase: true, out var role))
            return 0;
        return await _db.ApprovalItems.CountAsync(i => i.AssignedTo == role && i.Status == ApprovalStatus.Pending, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}