using Microsoft.EntityFrameworkCore;

using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Repositories;

public sealed class ClaimRepository : IClaimRepository
{
    private readonly Data.AppDbContext _db;

    public ClaimRepository(Data.AppDbContext db)
    {
        _db = db;
    }

    public async Task<Claim?> GetByNumberAsync(string claimNumber, CancellationToken ct)
        => await _db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.ClaimNumber == claimNumber, ct);

    public async Task<Claim?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Claim>> GetAllAsync(CancellationToken ct)
        => await _db.Claims.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public async Task<AdjudicationRun> CreateRunAsync(Guid claimId, CancellationToken ct)
    {
        var run = new AdjudicationRun
        {
            ClaimId = claimId,
            Claim = (await GetByIdAsync(claimId, ct)) ?? throw new Domain.Exceptions.DomainException($"Claim {claimId} not found."),
            Status = RunStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        _db.AdjudicationRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<AdjudicationRun?> GetRunAsync(Guid runId, CancellationToken ct)
        => await _db.AdjudicationRuns
            .Include(r => r.AgentRuns)
            .Include(r => r.Anomalies)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public Task AddAsync(Claim claim, CancellationToken ct)
    {
        _db.Claims.Add(claim);
        return _db.SaveChangesAsync(ct);
    }
}