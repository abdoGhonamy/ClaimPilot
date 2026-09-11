using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Interfaces.Repositories;

public interface IPolicyRepository
{
    Task<Policy?> GetByPolicyNumberAsync(string policyNumber, CancellationToken ct);
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PolicyVersion?> GetApplicableVersionAsync(Guid policyId, DateTime incidentDate, CancellationToken ct);
    Task<PolicyVersion?> GetVersionAsync(Guid policyId, int version, CancellationToken ct);
    Task<IReadOnlyList<PolicyVersion>> GetVersionsAsync(Guid policyId, CancellationToken ct);
    Task<IReadOnlyList<CoverageItem>> GetCoverageItemsAsync(Guid versionId, CancellationToken ct);
    Task<IReadOnlyList<Exclusion>> GetExclusionsAsync(Guid versionId, CancellationToken ct);
    Task AddAsync(Policy policy, CancellationToken ct);
    Task AddVersionAsync(PolicyVersion version, CancellationToken ct);
}

public interface IClaimRepository
{
    Task<Claim?> GetByNumberAsync(string claimNumber, CancellationToken ct);
    Task<Claim?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Claim>> GetAllAsync(CancellationToken ct);
    Task<AdjudicationRun> CreateRunAsync(Guid claimId, CancellationToken ct);
    Task<AdjudicationRun?> GetRunAsync(Guid runId, CancellationToken ct);
    Task<Decision?> GetDecisionForRunAsync(Guid runId, CancellationToken ct);
    Task AddLetterAsync(DecisionLetter letter, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
    Task AddAsync(Claim claim, CancellationToken ct);
}

public interface IChunkRepository
{
    Task<bool> ExistsByHashAsync(string contentHash, CancellationToken ct);
    Task AddAsync(PolicyChunk chunk, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<PolicyChunk> chunks, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IApprovalRepository
{
    Task<ApprovalItem?> GetAsync(Guid id, CancellationToken ct, bool includeHistory = false);
    Task<ApprovalItem> AddAsync(ApprovalItem item, CancellationToken ct);
    Task AddHistoryAsync(ApprovalHistory history, CancellationToken ct);
    Task<IReadOnlyList<ApprovalItem>> QueryAsync(
        ApprovalStatus? status, string? assigneeId, Priority? priority, CancellationToken ct);
    Task<int> FindUserQueueCountAsync(string assigneeId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IAuditRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct);
    Task<IReadOnlyList<AuditLog>> QueryAsync(string? entityType, Guid? entityId, string? runId, int take, CancellationToken ct);
}

public interface ITraceRepository
{
    Task AddAsync(TraceEntry entry, CancellationToken ct);
    Task<IReadOnlyList<TraceEntry>> GetByRunAsync(string runId, CancellationToken ct);
}

public sealed record TraceEntry(
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

public interface IUsageRepository
{
    Task AddAsync(UsageRecordEntry entry, CancellationToken ct);
    Task<IReadOnlyList<UsageRecordEntry>> GetByRunAsync(string runId, CancellationToken ct);
    Task<decimal> SumRunCostAsync(string runId, CancellationToken ct);
}

public sealed record UsageRecordEntry(
    string Scope, string Provider, string Model, int InputTokens, int OutputTokens, int TotalTokens,
    decimal EstimatedCostUsd, DateTime TimestampUtc, string? RunId = null, string? CorrelationId = null);