using Microsoft.EntityFrameworkCore;

using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Repositories;

public sealed class PolicyRepository : IPolicyRepository
{
    private readonly Data.AppDbContext _db;

    public PolicyRepository(Data.AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Policy>> GetActiveAsync(CancellationToken ct)
        => await _db.Policies.AsNoTracking()
            .Where(p => p.Status == PolicyStatus.Active)
            .OrderBy(p => p.ProductLine)
            .ThenBy(p => p.PolicyNumber)
            .ToListAsync(ct);

    public async Task<Policy?> GetByPolicyNumberAsync(string policyNumber, CancellationToken ct)
        => await _db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.PolicyNumber == policyNumber, ct);

    public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>
    /// Version-aware selection: the latest version whose EffectiveDate is on or
    /// before the incident date. Never the "newest" version blindly.
    /// </summary>
    public async Task<PolicyVersion?> GetApplicableVersionAsync(Guid policyId, DateTime incidentDate, CancellationToken ct)
        => ClaimPilot.Domain.Services.ApplicableVersionRule.Select(
            await _db.PolicyVersions.AsNoTracking()
                .Where(v => v.PolicyId == policyId)
                .ToListAsync(ct),
            incidentDate);

    public async Task<PolicyVersion?> GetVersionAsync(Guid policyId, int version, CancellationToken ct)
        => await _db.PolicyVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.PolicyId == policyId && v.Version == version, ct);

    public async Task<IReadOnlyList<PolicyVersion>> GetVersionsAsync(Guid policyId, CancellationToken ct)
        => await _db.PolicyVersions.AsNoTracking()
            .Where(v => v.PolicyId == policyId)
            .OrderBy(v => v.EffectiveDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CoverageItem>> GetCoverageItemsAsync(Guid versionId, CancellationToken ct)
        => await _db.CoverageItems.AsNoTracking()
            .Where(c => c.PolicyVersionId == versionId && c.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Exclusion>> GetExclusionsAsync(Guid versionId, CancellationToken ct)
        => await _db.Exclusions.AsNoTracking()
            .Where(e => e.PolicyVersionId == versionId && e.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PolicyChunk>> GetChunksAsync(Guid versionId, CancellationToken ct)
        => await _db.PolicyChunks.AsNoTracking()
            .Where(chunk => chunk.PolicyVersionId == versionId)
            .OrderBy(chunk => chunk.Page)
            .ThenBy(chunk => chunk.Section)
            .ThenBy(chunk => chunk.Clause)
            .ToListAsync(ct);

    public Task AddAsync(Policy policy, CancellationToken ct)
    {
        _db.Policies.Add(policy);
        return _db.SaveChangesAsync(ct);
    }

    public Task AddVersionAsync(PolicyVersion version, CancellationToken ct)
    {
        _db.PolicyVersions.Add(version);
        return _db.SaveChangesAsync(ct);
    }

    public async Task<StructuredSeedResult> SeedStructuredDataAsync(
        Guid policyVersionId,
        IEnumerable<CoverageItem> coverage,
        IEnumerable<Exclusion> exclusions,
        CancellationToken ct)
    {
        var coverageList = coverage.ToList();
        var exclusionList = exclusions.ToList();

        var existingCoverageCodes = await _db.CoverageItems.AsNoTracking()
            .Where(c => c.PolicyVersionId == policyVersionId)
            .Select(c => c.Code)
            .ToListAsync(ct);

        var existingExclusionCodes = await _db.Exclusions.AsNoTracking()
            .Where(e => e.PolicyVersionId == policyVersionId)
            .Select(e => e.Code)
            .ToListAsync(ct);

        var coverageSet = existingCoverageCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exclusionSet = existingExclusionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var addedCoverage = coverageList.Where(c => !coverageSet.Contains(c.Code)).ToList();
        var addedExclusions = exclusionList.Where(e => !exclusionSet.Contains(e.Code)).ToList();

        if (addedCoverage.Count > 0)
            _db.CoverageItems.AddRange(addedCoverage);

        if (addedExclusions.Count > 0)
            _db.Exclusions.AddRange(addedExclusions);

        await _db.SaveChangesAsync(ct);

        return new StructuredSeedResult(
            addedCoverage.Count, coverageList.Count - addedCoverage.Count,
            addedExclusions.Count, exclusionList.Count - addedExclusions.Count);
    }
}
