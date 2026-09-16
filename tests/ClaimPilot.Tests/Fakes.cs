using System.Runtime.CompilerServices;
using System.Text.Json;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Documents;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Interfaces.Retrieval;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Application.Services;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.ValueObjects;
using ClaimsPrincipal = System.Security.Claims.ClaimsPrincipal;

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

#pragma warning disable CS0067 // event required by ILLMProvider; deliberately never raised in the stub
    public event UsageRecordedHandler? UsageRecorded;
#pragma warning restore CS0067

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
        [EnumeratorCancellation] CancellationToken ct)
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

public sealed class FakeAuthorityService : IAuthorityService
{
    public Task<bool> CanApproveAsync(ClaimsPrincipal user, decimal amount, string action, CancellationToken ct)
        => Task.FromResult(true);

    public decimal GetThresholdForUser(ClaimsPrincipal user) => 1_000_000_000m;

    public decimal GetThresholdForRole(string role) => role switch
    {
        "Adjuster" => 10_000m,
        "Supervisor" => 100_000m,
        _ => 1_000_000_000m
    };
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
        ApprovalStatus? status, AssigneeRole? assigneeId, Priority? priority, CancellationToken ct)
    {
        var query = _items.AsEnumerable();
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        if (assigneeId.HasValue) query = query.Where(i => i.AssignedTo == assigneeId.Value);
        return Task.FromResult<IReadOnlyList<ApprovalItem>>(query.ToList());
    }

    public Task<int> FindUserQueueCountAsync(AssigneeRole? assigneeId, CancellationToken ct)
        => Task.FromResult(_items.Count(i => i.AssignedTo == assigneeId && i.Status == ApprovalStatus.Pending));

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class FakeClaimRepository : IClaimRepository
{
    public List<Claim> Claims { get; } = new();
    public List<ClaimDocument> Documents { get; } = new();
    public string? NextClaimNumber { get; set; }

    public Task<Claim?> GetByNumberAsync(string claimNumber, CancellationToken ct)
        => Task.FromResult(Claims.FirstOrDefault(c => c.ClaimNumber == claimNumber));

    public Task<Claim?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Claims.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Claim>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Claim>>(Claims.ToList());

    public Task<AdjudicationRun> CreateRunAsync(Guid claimId, CancellationToken ct)
    {
        var claim = Claims.FirstOrDefault(c => c.Id == claimId)
            ?? throw new NotSupportedException();
        var run = new AdjudicationRun
        {
            Id = Guid.NewGuid(),
            ClaimId = claimId,
            Claim = claim,
            Status = RunStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        claim.Runs.Add(run);
        return Task.FromResult(run);
    }

    public Task<AdjudicationRun?> GetRunAsync(Guid runId, CancellationToken ct)
        => Task.FromResult(Claims.SelectMany(c => c.Runs).FirstOrDefault(r => r.Id == runId));

    public Task<Decision?> GetDecisionForRunAsync(Guid runId, CancellationToken ct) => Task.FromResult<Decision?>(null);
    public Task AddLetterAsync(DecisionLetter letter, CancellationToken ct) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    public Task AddAsync(Claim claim, CancellationToken ct)
    {
        Claims.Add(claim);
        return Task.CompletedTask;
    }

    public Task<string> GenerateClaimNumberAsync(CancellationToken ct)
        => Task.FromResult(NextClaimNumber ?? $"CLAIM-{DateTime.UtcNow.Year}-{Claims.Count + 1:D3}");

    public Task<ClaimDocument> AddDocumentAsync(ClaimDocument document, CancellationToken ct)
    {
        Documents.Add(document);
        var claim = Claims.FirstOrDefault(c => c.Id == document.ClaimId);
        claim?.Documents.Add(document);
        return Task.FromResult(document);
    }

    public Task<bool> HasDocumentsAsync(Guid claimId, CancellationToken ct)
        => Task.FromResult(Documents.Any(d => d.ClaimId == claimId));
}

public sealed class FakeToolRegistry : IToolRegistry
{
    public List<ToolCallRecord> Calls { get; } = new();
    public IReadOnlyList<CoverageLine> CoverageItems { get; init; } = Array.Empty<CoverageLine>();
    public IReadOnlyList<PolicyExclusionLine> Exclusions { get; init; } = Array.Empty<PolicyExclusionLine>();

    public IReadOnlyList<ToolDefinition> Definitions => Array.Empty<ToolDefinition>();

    public ToolDefinition GetDefinition(ToolName name) =>
        new() { Name = name, Description = name.ToString() };

    public Task<ToolCallRecord> ExecuteAsync(
        ToolName name,
        AgentType agentType,
        IReadOnlyDictionary<string, string> parameters,
        Guid runId,
        string? correlationId,
        CancellationToken ct)
    {
        var outputJson = name switch
        {
            ToolName.RetrievePolicyVersioned => JsonSerializer.Serialize(new PolicyMatchResult(
                Guid.NewGuid(), Guid.NewGuid(), 1, new DateTime(2022, 1, 1),
                CoverageItems, Exclusions)),
            ToolName.ListCoverageItems => JsonSerializer.Serialize(CoverageItems),
            ToolName.CheckExclusion => JsonSerializer.Serialize(new ExclusionCheckResult(false, null, null, null)),
            ToolName.RecordAnomaly => JsonSerializer.Serialize(new { written = true }),
            ToolName.DraftAdjudication => JsonSerializer.Serialize(new DraftAdjudicationResult(true, "pending-1", null)),
            _ => "{}"
        };

        var record = new ToolCallRecord
        {
            Id = Guid.NewGuid(),
            Tool = name,
            InputJson = JsonSerializer.Serialize(parameters),
            OutputJson = outputJson,
            Succeeded = true,
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow
        };
        Calls.Add(record);
        return Task.FromResult(record);
    }
}

public sealed class FakeStorageService : IStorageService
{
    public List<(Guid ClaimId, Guid DocumentId, string Extension)> Saved { get; } = new();
    public long SizeBytes { get; init; } = 42;

    public Task<StoredFile> SaveAsync(Guid claimId, Guid documentId, string extension, Stream content, CancellationToken ct)
    {
        Saved.Add((claimId, documentId, extension));
        var relative = Path.Combine(claimId.ToString("N"), $"{documentId:N}{extension}");
        return Task.FromResult(new StoredFile(relative, SizeBytes));
    }
}

public sealed class NullTraceViewBuilder : IRunTraceViewBuilder
{
    public Task<RunTraceView> BuildAsync(string runId, CancellationToken ct)
        => Task.FromResult<RunTraceView>(null!);
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
public sealed class FakeApprovalQueueReader : IApprovalQueueReader
{
    private readonly Dictionary<Guid, ApprovalItemDetail> _items = new();
    private readonly List<ApprovalItemView> _views = new();

    public void AddItem(ApprovalItemDetail item) => _items[item.Id] = item;
    public void AddView(ApprovalItemView item) => _views.Add(item);

    public Task<IReadOnlyList<ApprovalItemView>> GetQueueAsync(ReviewQueueFilter filter, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ApprovalItemView>>(_views.ToList());

    public Task<ApprovalItemDetail?> GetAsync(Guid approvalItemId, CancellationToken ct)
        => Task.FromResult(_items.TryGetValue(approvalItemId, out var item) ? item : null);
}
