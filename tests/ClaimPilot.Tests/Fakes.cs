using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Retrieval;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Application.Services;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.ValueObjects;

namespace ClaimPilot.Tests;

public sealed record TrapPolicyVersions(Policy Policy, PolicyVersion V1, PolicyVersion V2);

public static class TestCorpus
{
    public static Guid PolicyId { get; } = Guid.NewGuid();

    public static TrapPolicyVersions BuildAutoVersionTrap()
    {
        var policy = new Policy
        {
            Id = PolicyId,
            PolicyNumber = "AUT-2022",
            ProductLine = "AUT",
            Name = "Auto Comprehensive 2022",
            Status = PolicyStatus.Active
        };

        var v1 = new PolicyVersion
        {
            Id = Guid.NewGuid(),
            PolicyId = policy.Id,
            Policy = policy,
            Version = 1,
            EffectiveDate = new DateTime(2022, 1, 1),
            Status = PolicyVersionStatus.Superseded
        };
        var v2 = new PolicyVersion
        {
            Id = Guid.NewGuid(),
            PolicyId = policy.Id,
            Policy = policy,
            Version = 2,
            EffectiveDate = new DateTime(2025, 6, 1),
            Status = PolicyVersionStatus.Active
        };
        policy.Versions.Add(v1);
        policy.Versions.Add(v2);

        return new TrapPolicyVersions(policy, v1, v2);
    }
}

public sealed class FakePolicyRepository : IPolicyRepository
{
    private readonly List<Policy> _policies;

    public FakePolicyRepository(params Policy[] policies) => _policies = policies.ToList();

    public Task<Policy?> GetByPolicyNumberAsync(string policyNumber, CancellationToken ct)
        => Task.FromResult(_policies.FirstOrDefault(p => p.PolicyNumber == policyNumber));

    public Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_policies.FirstOrDefault(p => p.Id == id));

    public Task<PolicyVersion?> GetApplicableVersionAsync(Guid policyId, DateTime incidentDate, CancellationToken ct)
    {
        var policy = _policies.FirstOrDefault(p => p.Id == policyId);
        var versions = policy?.Versions.Count > 0 ? policy.Versions : policy?.Versions.ToList() ?? new List<PolicyVersion>();
        return Task.FromResult(ClaimPilot.Domain.Services.ApplicableVersionRule.Select(versions, incidentDate));
    }

    public Task<PolicyVersion?> GetVersionAsync(Guid policyId, int version, CancellationToken ct)
        => Task.FromResult(_policies
            .Where(p => p.Id == policyId)
            .SelectMany(p => p.Versions)
            .FirstOrDefault(v => v.Version == version));

    public Task<IReadOnlyList<PolicyVersion>> GetVersionsAsync(Guid policyId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PolicyVersion>>(_policies
            .Where(p => p.Id == policyId)
            .SelectMany(p => p.Versions)
            .OrderBy(v => v.EffectiveDate)
            .ToList());

    public Task<IReadOnlyList<CoverageItem>> GetCoverageItemsAsync(Guid versionId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CoverageItem>>(new List<CoverageItem>());

    public Task<IReadOnlyList<Exclusion>> GetExclusionsAsync(Guid versionId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Exclusion>>(new List<Exclusion>());

    public Task AddAsync(Policy policy, CancellationToken ct)
    {
        _policies.Add(policy);
        return Task.CompletedTask;
    }

    public Task AddVersionAsync(PolicyVersion version, CancellationToken ct)
        => Task.CompletedTask;
}

public sealed class FakeRetrievalService : IRetrievalService
{
    private readonly IReadOnlyList<RetrievedChunk> _chunks;
    public bool Sufficient { get; init; } = true;
    public RetrievalQuery? LastQuery { get; private set; }
    public int RetrieveCalls { get; private set; }

    public FakeRetrievalService(IReadOnlyList<RetrievedChunk> chunks) => _chunks = chunks;

    public Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        LastQuery = query;
        RetrieveCalls++;
        return Task.FromResult(new RetrievalResult
        {
            Chunks = _chunks,
            Sufficient = Sufficient,
            RetrievalTraceId = $"trace-{query.PolicyVersionId}"
        });
    }

    public Task<AskResult> AskAsync(string question, string policyNumber, DateTime? incidentDate, CancellationToken ct)
        => throw new NotSupportedException();
}

public sealed class FakeLLMProvider : ILLMProvider
{
    public string ProviderName => "stub";
    public string ModelName => "stub-model";
    public string? NextText { get; set; } = "plain stub answer";
    public int CompleteCalls { get; private set; }
    public event UsageRecordedHandler? UsageRecorded;

    public Task<LLMResult> CompleteAsync(
        string systemPrompt,
        string userContent,
        IReadOnlyList<ChatMessage>? history,
        CancellationToken ct)
    {
        CompleteCalls++;
        return Task.FromResult(new LLMResult(
            NextText ?? string.Empty, 10, 10, ModelName, ProviderName));
    }

    public async IAsyncEnumerable<LLMResult> StreamCompleteAsync(
        string systemPrompt,
        string userContent,
        IReadOnlyList<ChatMessage>? history,
        CancellationToken ct)
    {
        await Task.Yield();
        yield return new LLMResult(NextText ?? string.Empty, 10, 10, ModelName, ProviderName);
    }
}

public sealed class FakeTraceService : ITraceService
{
    public Task WriteAsync(
        string runId, string entityType, string action, string? entityId = null,
        string? actorId = null, string? before = null, string? after = null,
        string? correlationId = null, string? message = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<TraceRecord>> GetRunAsync(string runId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TraceRecord>>(new List<TraceRecord>());
}

public sealed class FakeUsageTracker : IUsageTracker
{
    public List<UsageRecord> Records { get; } = new();

    public Task RecordAsync(UsageRecord usage, CancellationToken ct)
    {
        Records.Add(usage);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageRecord>> GetForRunAsync(string runId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<UsageRecord>>(Records);

    public Task<decimal> EstimateRunCostAsync(string runId, CancellationToken ct)
        => Task.FromResult(0m);
}

public sealed class FakeAuditService : IAuditService
{
    public List<AuditLogEntry> Entries { get; } = new();

    public Task RecordAsync(AuditLogEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLog>> QueryAsync(
        string? entityType = null, Guid? entityId = null, string? runId = null,
        int take = 100, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AuditLog>>(new List<AuditLog>());
}

public sealed class FakeApprovalRepository : IApprovalRepository
{
    private readonly List<ApprovalItem> _items = new();

    public void AddSeed(ApprovalItem item) => _items.Add(item);

    public Task<ApprovalItem?> GetAsync(Guid id, CancellationToken ct, bool includeHistory = false)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        return Task.FromResult(item);
    }

    public Task<ApprovalItem> AddAsync(ApprovalItem item, CancellationToken ct)
    {
        _items.Add(item);
        return Task.FromResult(item);
    }

    public Task AddHistoryAsync(ApprovalHistory history, CancellationToken ct)
    {
        var item = _items.FirstOrDefault(i => i.Id == history.ApprovalItemId);
        item?.History.Add(history);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApprovalItem>> QueryAsync(
        ApprovalStatus? status, string? assigneeId, Priority? priority, CancellationToken ct)
    {
        var query = _items.AsEnumerable();
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        return Task.FromResult<IReadOnlyList<ApprovalItem>>(query.ToList());
    }

    public Task<int> FindUserQueueCountAsync(string assigneeId, CancellationToken ct)
        => Task.FromResult(_items.Count(i => i.AssignedTo == assigneeId && i.Status == ApprovalStatus.Pending));

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class FakeClaimRepository : IClaimRepository
{
    public Task<Claim?> GetByNumberAsync(string claimNumber, CancellationToken ct) => Task.FromResult<Claim?>(null);
    public Task<Claim?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Claim?>(null);
    public Task<IReadOnlyList<Claim>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Claim>>(new List<Claim>());
    public Task<AdjudicationRun> CreateRunAsync(Guid claimId, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<AdjudicationRun?> GetRunAsync(Guid runId, CancellationToken ct) => Task.FromResult<AdjudicationRun?>(null);
    public Task<Decision?> GetDecisionForRunAsync(Guid runId, CancellationToken ct) => Task.FromResult<Decision?>(null);
    public Task AddLetterAsync(DecisionLetter letter, CancellationToken ct) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    public Task AddAsync(Claim claim, CancellationToken ct) => Task.CompletedTask;
}

public static class Chunk
{
    public static RetrievedChunk Make(DateTime effectiveDate, int version, string section, string text)
        => new()
        {
            ChunkId = Guid.NewGuid().ToString(),
            PolicyId = TestCorpus.PolicyId.ToString(),
            PolicyNumber = "AUT-2022",
            Version = version,
            EffectiveDate = effectiveDate,
            Section = section,
            Clause = "C.1",
            Page = 1,
            Text = text,
            Citation = new Citation
            {
                ChunkId = Guid.NewGuid().ToString(),
                PolicyId = TestCorpus.PolicyId.ToString(),
                Version = version,
                Section = section,
                Clause = "C.1",
                Page = 1,
                TextExcerpt = text,
                Source = "corpus"
            },
            Score = 0.9f,
            DenseRank = 1,
            KeywordRank = 1
        };
}