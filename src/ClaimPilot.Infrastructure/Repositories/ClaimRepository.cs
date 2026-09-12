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
        => await _db.Claims
            .Include(c => c.Runs)
            .ThenInclude(r => r.ApprovalItems)
            .Include(c => c.Runs)
            .ThenInclude(r => r.Anomalies)
            .Include(c => c.Runs)
            .ThenInclude(r => r.FinalDecision)
            .Include(c => c.Documents)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Claim>> GetAllAsync(CancellationToken ct)
        => await _db.Claims.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public async Task<AdjudicationRun> CreateRunAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await _db.Claims.FirstOrDefaultAsync(c => c.Id == claimId, ct)
            ?? throw new Domain.Exceptions.DomainException($"Claim {claimId} not found.");
        var run = new AdjudicationRun
        {
            ClaimId = claimId,
            Claim = claim,
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
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<Decision?> GetDecisionForRunAsync(Guid runId, CancellationToken ct)
        => await _db.Decisions
            .Include(d => d.AdjudicationRun)
            .ThenInclude(r => r.Claim)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(d => d.AdjudicationRunId == runId, ct);

    public async Task AddLetterAsync(DecisionLetter letter, CancellationToken ct)
    {
        _db.DecisionLetters.Add(letter);
        await _db.SaveChangesAsync(ct);
    }

    public Task AddAsync(Claim claim, CancellationToken ct)
    {
        _db.Claims.Add(claim);
        return _db.SaveChangesAsync(ct);
    }

    public async Task<string> GenerateClaimNumberAsync(CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"CLAIM-{year}-";

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var maxNumber = await _db.Claims
            .Where(c => c.ClaimNumber.StartsWith(prefix))
            .OrderByDescending(c => c.ClaimNumber)
            .Select(c => c.ClaimNumber)
            .FirstOrDefaultAsync(ct);

        int nextSequence = 1;
        if (maxNumber is not null)
        {
            var lastPart = maxNumber.Substring(prefix.Length);
            if (int.TryParse(lastPart, out var lastSeq))
                nextSequence = lastSeq + 1;
        }

        await tx.CommitAsync(ct);
        return $"{prefix}{nextSequence:D3}";
    }

    public async Task<ClaimDocument> AddDocumentAsync(ClaimDocument document, CancellationToken ct)
    {
        // Re-attach a tracked claim: GetByIdAsync is AsNoTracking, and attaching the
        // detached instance would make EF try to INSERT the claim (duplicate PK_Claims).
        var claim = await _db.Claims.FirstAsync(c => c.Id == document.ClaimId, ct);
        document.Claim = claim;
        _db.ClaimDocuments.Add(document);
        await _db.SaveChangesAsync(ct);
        return document;
    }

    public async Task<bool> HasDocumentsAsync(Guid claimId, CancellationToken ct)
        => await _db.ClaimDocuments.AnyAsync(d => d.ClaimId == claimId, ct);
}