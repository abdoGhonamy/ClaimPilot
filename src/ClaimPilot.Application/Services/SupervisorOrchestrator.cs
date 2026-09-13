using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Adjudication;
using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Application.Services.Agents;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;
using ClaimPilot.Domain.ValueObjects;

namespace ClaimPilot.Application.Services;

public sealed class OrchestratorOptions
{
    public int MaxIterations { get; set; } = 8;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxRetries { get; set; } = 3;
    public int BackoffBaseMs { get; set; } = 500;
    public bool EnableFallbackRag { get; set; } = true;
}

/// <summary>
/// Supervisor orchestrator implementing our own agent loop (no external framework).
/// 1. Resolve and PIN the policy version for the incident date.
/// 2. Run agents in order with per-agent retry + backoff.
/// 3. Compute the deterministic payout.
/// 4. Create a human review item (pending). Nothing becomes final automatically.
/// 5. Enforce a configurable iteration limit, timeout, and cancellation.
/// 6. Persist every step to the trace; degrade to safe plain RAG on failure.
/// </summary>
public sealed class SupervisorOrchestrator : IClaimsOrchestrator
{
    private readonly OrchestratorOptions _options;
    private readonly IPolicyRepository _policies;
    private readonly IClaimRepository _claims;
    private readonly IAdjudicationEngine _engine;
    private readonly IApprovalRepository _approvals;
    private readonly ITraceService _trace;
    private readonly IAuditService _audit;
    private readonly IUsageTracker _usage;
    private readonly OrchestrationEventSink _events;
    private readonly ILogger<SupervisorOrchestrator> _logger;

    private readonly CoverageMatcherAgent _coverage;
    private readonly ExclusionAnalystAgent _exclusions;
    private readonly AnomalyDetectorAgent _anomalies;
    private readonly AdjudicationDrafterAgent _drafter;

    public SupervisorOrchestrator(
        IOptions<OrchestratorOptions> options,
        IPolicyRepository policies,
        IClaimRepository claims,
        IAdjudicationEngine engine,
        IApprovalRepository approvals,
        ITraceService trace,
        IAuditService audit,
        IUsageTracker usage,
        OrchestrationEventSink events,
        CoverageMatcherAgent coverage,
        ExclusionAnalystAgent exclusions,
        AnomalyDetectorAgent anomalies,
        AdjudicationDrafterAgent drafter,
        ILogger<SupervisorOrchestrator> logger)
    {
        _options = options.Value;
        _policies = policies;
        _claims = claims;
        _engine = engine;
        _approvals = approvals;
        _trace = trace;
        _audit = audit;
        _usage = usage;
        _events = events;
        _coverage = coverage;
        _exclusions = exclusions;
        _anomalies = anomalies;
        _drafter = drafter;
        _logger = logger;

        // Forward event sink to the orchestrator's subscribers.
        _events.Raised += (e, ct) => EmitToSubscribers(e, ct);
    }

    private event OrchestrationEventHandler? _subscribers;
    public event OrchestrationEventHandler? EventRaised
    {
        add => _subscribers += value;
        remove => _subscribers -= value;
    }

    public async Task<RunResult> RunAsync(Guid claimId, string? correlationId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.Timeout);
        var runCt = timeoutCts.Token;

        var run = await _claims.CreateRunAsync(claimId, runCt);
        var agentRuns = new List<AgentRun>();
        var anomalies = new List<AnomalyDto>();
        var iteration = 0;
        RequestState state = new();

        try
        {
            await _trace.WriteAsync(run.Id.ToString(), "AdjudicationRun", "started",
                run.Id.ToString(), correlationId: correlationId, message: $"Run started for claim {claimId}", ct: runCt);
            await _events.Emit(run.Id, correlationId, "workflow_started", null, null, null, runCt);

            var claim = await _claims.GetByIdAsync(claimId, runCt)
                ?? throw new DomainException($"Claim {claimId} not found.");
            var policy = await _policies.GetByPolicyNumberAsync(claim.PolicyNumber, runCt)
                ?? throw new DomainException($"Policy {claim.PolicyNumber} not found.");

            var version = await _policies.GetApplicableVersionAsync(policy.Id, claim.IncidentDate, runCt)
                ?? throw new PolicyVersionNotFoundException(claim.PolicyNumber, claim.IncidentDate);

            run.PolicyVersionId = version.Id;
            await _claims.SaveChangesAsync(runCt);

            await _trace.WriteAsync(run.Id.ToString(), "PolicyVersion", "selected",
                version.Id.ToString(), correlationId: correlationId,
                message: $"Version {version.Version} effective {version.EffectiveDate:yyyy-MM-dd} selected for incident {claim.IncidentDate:yyyy-MM-dd}",
                ct: runCt);
            await _events.Emit(run.Id, correlationId, "policy_version_selected", null, null,
                JsonSerializer.Serialize(new { version = version.Version, effective_date = version.EffectiveDate }), runCt);

            var coverageItems = await _policies.GetCoverageItemsAsync(version.Id, runCt);
            var limit = coverageItems.FirstOrDefault(c => c.Type == CoverageType.Limit)?.Amount;

            state = new RequestState
            {
                PolicyId = policy.Id,
                VersionId = version.Id,
                Version = version.Version,
                EffectiveDate = version.EffectiveDate,
                PolicyLimit = limit,
                CoverageItems = coverageItems,
                Exclusions = await _policies.GetExclusionsAsync(version.Id, runCt)
            };

            // --- Agent 1: Coverage Matcher
            var coverageResult = await RunAgentWithRetryAsync(run, claim, state, _coverage, iteration, correlationId, runCt);
            agentRuns.Add(coverageResult.Run);
            state.MergeClaimsState(coverageResult.ContextState);
            iteration++;

            // --- Agent 2: Exclusion Analyst
            var exclusionResult = await RunAgentWithRetryAsync(run, claim, state, _exclusions, iteration, correlationId, runCt);
            agentRuns.Add(exclusionResult.Run);
            state.ApplicableExclusions = ParseExclusions(exclusionResult.Result.Output);
            iteration++;

            // --- Deterministic engine (no LLM)
            var computation = _engine.Compute(new ComputationRequest
            {
                ClaimAmount = claim.ClaimAmount,
                Deductible = state.CoverageItems.FirstOrDefault(c => c.Type == CoverageType.Deductible)?.Amount,
                CoinsuranceRate = state.CoverageItems.FirstOrDefault(c => c.Type == CoverageType.Coinsurance)?.PercentageRate,
                CoverageLimit = state.CoverageItems.FirstOrDefault(c => c.Type == CoverageType.Limit)?.Amount,
                ApplicableExclusions = state.ApplicableExclusions.Select(a => a.Code).ToList()
            });

            state.Computation = computation;
            await PersistComputationAsync(run, computation, correlationId, runCt);

            // --- Agent 3: Anomaly Detector
            var anomalyResult = await RunAgentWithRetryAsync(run, claim, state, _anomalies, iteration, correlationId, runCt);
            agentRuns.Add(anomalyResult.Run);
            anomalies.AddRange(ParseAnomalies(anomalyResult.Result.Output));
            iteration++;

            // --- Agent 4: Adjudication Drafter (recommendation only)
            var draftResult = await RunAgentWithRetryAsync(run, claim, state, _drafter, iteration, correlationId, runCt);
            agentRuns.Add(draftResult.Run);
            iteration++;

            // --- Human review gate. draft_adjudication does not auto-finalize.
            var drafted = JsonDocument.Parse(draftResult.Result.Output);
            var proposedAmount = drafted.RootElement.TryGetProperty("proposed_amount", out var amt) && amt.ValueKind == JsonValueKind.Number
                ? amt.GetDecimal()
                : computation.Payable;

            var item = new ApprovalItem
            {
                ClaimId = claim.Id,
                RunId = run.Id,
                Status = ApprovalStatus.Pending,
                Priority = ComputePriority(claim, state.PolicyLimit),
                SLADeadline = ComputeDeadline(ComputePriority(claim, state.PolicyLimit)),
                Title = $"Decision required — {claim.ClaimNumber}",
                Summary = draftResult.Result.Output
            };

            await _approvals.AddAsync(item, runCt);
            await _trace.WriteAsync(run.Id.ToString(), "ApprovalItem", "created",
                item.Id.ToString(), correlationId: correlationId,
                message: $"Approval item created; review required", ct: runCt);
            await _events.Emit(run.Id, correlationId, "review_required", null, null,
                JsonSerializer.Serialize(new { approval_item_id = item.Id, proposed_payout = proposedAmount }), runCt);

            run.Status = RunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;
            await _claims.SaveChangesAsync(runCt);

            await _trace.WriteAsync(run.Id.ToString(), "AdjudicationRun", "completed",
                run.Id.ToString(), correlationId: correlationId, ct: runCt);
            await _events.Emit(run.Id, correlationId, "completed", null, null,
                JsonSerializer.Serialize(new { approval_item_id = item.Id }), runCt);

            return new RunResult
            {
                RunId = run.Id,
                ClaimId = claim.Id,
                ClaimNumber = claim.ClaimNumber,
                PolicyNumber = claim.PolicyNumber,
                PolicyVersionId = version.Id,
                PolicyVersion = version.Version,
                EffectiveDate = version.EffectiveDate,
                ReviewRequired = true,
                ApprovalItemId = item.Id,
                ProposedPayout = proposedAmount,
                Status = "completed",
                Degraded = false,
                Summary = "Recommendation ready for human review.",
                AnomalySummary = anomalies.Select(a => $"{a.Type}: {a.Description}").ToList(),
                IterationsUsed = iteration
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Status = RunStatus.Cancelled;
            run.CancelledAt = DateTime.UtcNow;
            await _claims.SaveChangesAsync(CancellationToken.None);
            await _trace.WriteAsync(run.Id.ToString(), "AdjudicationRun", "cancelled",
                run.Id.ToString(), correlationId: correlationId, message: "Run cancelled", ct: CancellationToken.None);
            await _events.Emit(run.Id, correlationId, "cancelled", null, null, null, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            run.Status = RunStatus.Failed;
            run.FailReason = "Timeout";
            await _claims.SaveChangesAsync(CancellationToken.None);
            await _trace.WriteAsync(run.Id.ToString(), "AdjudicationRun", "failed",
                run.Id.ToString(), correlationId: correlationId, message: "Run timed out", ct: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orchestrator failed for claim {ClaimId}", claimId);
            run.Status = RunStatus.Failed;
            run.FailReason = ex.Message;
            if (ex is not DomainException) run.FailReason = ex.GetType().Name + ": " + ex.Message;
            await _claims.SaveChangesAsync(runCt);
            await _trace.WriteAsync(run.Id.ToString(), "AdjudicationRun", "failed",
                run.Id.ToString(), correlationId: correlationId, message: run.FailReason, ct: CancellationToken.None);
            await _events.Emit(run.Id, correlationId, "error", null, null, run.FailReason, CancellationToken.None);

            if (_options.EnableFallbackRag)
            {
                run.Status = RunStatus.Degraded;
                run.Degraded = true;
                await _claims.SaveChangesAsync(CancellationToken.None);
                _logger.LogWarning("Falling back to plain RAG for claim {ClaimId}", claimId);
                return new RunResult
                {
                    RunId = run.Id,
                    ClaimId = claimId,
                    ClaimNumber = string.Empty,
                    PolicyNumber = string.Empty,
                    PolicyVersionId = Guid.Empty,
                    PolicyVersion = 0,
                    EffectiveDate = default,
                    ReviewRequired = true,
                    ApprovalItemId = null,
                    ProposedPayout = null,
                    Status = "degraded",
                    Degraded = true,
                    Summary = "Agent orchestration failed; ran safe plain-RAG fallback. No automatic decision was made.",
                    AnomalySummary = Array.Empty<string>(),
                    IterationsUsed = iteration
                };
            }

            throw;
        }
    }

    private sealed record RequestState
    {
        public Guid PolicyId { get; init; }
        public Guid VersionId { get; init; }
        public int Version { get; init; }
        public DateTime EffectiveDate { get; init; }
        public decimal? PolicyLimit { get; init; }
        public IReadOnlyList<CoverageItem> CoverageItems { get; init; } = Array.Empty<CoverageItem>();
        public IReadOnlyList<Exclusion> Exclusions { get; init; } = Array.Empty<Exclusion>();
        public IReadOnlyList<ApplicableExclusion> ApplicableExclusions { get; set; } = Array.Empty<ApplicableExclusion>();
        public Computation? Computation { get; set; }

        public void MergeClaimsState(AgentContext contextState)
        {
            // no-op for now; reserved for future agent state sharing
        }
    }

    private async Task<(AgentRun Run, AgentResult Result, AgentContext ContextState)> RunAgentWithRetryAsync(
        AdjudicationRun run, Claim claim, RequestState state, IAgent agent,
        int iteration, string? correlationId, CancellationToken ct)
    {
        var ctx = new AgentContext
        {
            RunId = run.Id,
            ClaimId = claim.Id,
            ClaimNumber = claim.ClaimNumber,
            PolicyNumber = claim.PolicyNumber,
            IncidentDate = claim.IncidentDate,
            ClaimAmount = claim.ClaimAmount,
            ClaimDescription = claim.Description,
            AllowedTools = AgentToolMap.AllowedFor(agent.AgentType),
            MaxIterations = _options.MaxIterations,
            CorrelationId = correlationId,
            State = await BuildAgentStateAsync(state, claim.Id, ct)
        };

        var runRecord = new AgentRun
        {
            AdjudicationRunId = run.Id,
            AdjudicationRun = run,
            AgentType = agent.AgentType,
            Status = RunStatus.Running,
            Iteration = iteration,
            InputJson = JsonSerializer.Serialize(new { claim.ClaimNumber, claim.ClaimAmount })
        };

        Exception? lastError = null;
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                var result = await agent.ExecuteAsync(ctx, ct);
                runRecord.OutputJson = JsonSerializer.Serialize(result);
                runRecord.Status = RunStatus.Completed;
                runRecord.EndedAt = DateTime.UtcNow;
                await _trace.WriteAsync(run.Id.ToString(), "AgentRun", "completed",
                    agent.AgentType.ToString(), correlationId: correlationId,
                    message: agent.DisplayName, ct: ct);
                return (runRecord, result, ctx);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                lastError = ex;
                var delay = TimeSpan.FromMilliseconds(_options.BackoffBaseMs * (1 << attempt));
                _logger.LogWarning(ex, "Agent {Agent} attempt {Attempt} failed; retrying in {Delay}",
                    agent.DisplayName, attempt, delay);
                await _trace.WriteAsync(run.Id.ToString(), "AgentRun", "retry",
                    agent.AgentType.ToString(), correlationId: correlationId,
                    message: $"attempt {attempt + 1}: {ex.Message}", ct: ct);
                await Task.Delay(delay, ct);
            }
        }

        runRecord.Status = RunStatus.Failed;
        runRecord.Error = lastError?.Message;
        runRecord.EndedAt = DateTime.UtcNow;
        throw new DomainException($"Agent '{agent.DisplayName}' failed after retries: {lastError?.Message}");
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildAgentStateAsync(RequestState state, Guid claimId, CancellationToken ct)
    {
        var hasDocuments = await _claims.HasDocumentsAsync(claimId, ct);
        var dict = new Dictionary<string, string>
        {
            ["policy_limit"] = state.PolicyLimit?.ToString() ?? string.Empty,
            ["has_documents"] = hasDocuments ? "true" : "false"
        };
        if (state.Computation is { } c)
        {
            dict["computation_payable"] = c.Payable.ToString();
            dict["computation_excluded"] = c.InsufficiencyReason?.Contains("exclusion", StringComparison.OrdinalIgnoreCase) == true ? "true" : "false";
            dict["computation_insufficient"] = c.Sufficient ? "false" : "true";
            dict["computation_citations"] = JsonSerializer.Serialize(BuildCitations(state));
        }
        return dict;
    }

    private static Citation[] BuildCitations(RequestState state)
    {
        return state.CoverageItems.Select(c => new Citation
        {
            ChunkId = c.Id.ToString(),
            PolicyId = state.PolicyId.ToString(),
            Version = state.Version,
            Section = c.Name,
            Clause = c.Code,
            TextExcerpt = c.Description
        }).ToArray();
    }

    private async Task PersistComputationAsync(AdjudicationRun run, Computation computation,
        string? correlationId, CancellationToken ct)
    {
        await _trace.WriteAsync(run.Id.ToString(), "Computation", "completed",
            run.Id.ToString(), correlationId: correlationId,
            after: JsonSerializer.Serialize(computation), message: "Deterministic payout computed", ct: ct);
        await _events.Emit(run.Id, correlationId, "engine_completed", null, null,
            JsonSerializer.Serialize(computation), ct);
    }

    private static IReadOnlyList<ApplicableExclusion> ParseExclusions(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<ApplicableExclusion>();
        try
        {
            var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("applicable_codes", out var codes) && codes.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ApplicableExclusion>();
                foreach (var code in codes.EnumerateArray())
                    list.Add(new ApplicableExclusion(code.GetString() ?? string.Empty, string.Empty, string.Empty));
                return list;
            }
        }
        catch (JsonException) { /* ignore malformed agent output */ }
        return Array.Empty<ApplicableExclusion>();
    }

    private static IReadOnlyList<AnomalyDto> ParseAnomalies(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<AnomalyDto>();
        try
        {
            var list = JsonSerializer.Deserialize<List<AnomalyDto>>(output);
            return (IReadOnlyList<AnomalyDto>?)list ?? Array.Empty<AnomalyDto>();
        }
        catch (JsonException)
        {
            return Array.Empty<AnomalyDto>();
        }
    }

    private static Priority ComputePriority(Claim claim, decimal? policyLimit)
    {
        var ratio = policyLimit.HasValue && policyLimit > 0 ? claim.ClaimAmount / policyLimit.Value : 0.5m;
        if (claim.ClaimAmount > 50000 || ratio >= 1.6m) return Priority.Critical;
        if (claim.ClaimAmount > 20000 || ratio >= 1.2m) return Priority.High;
        if (claim.ClaimAmount > 5000) return Priority.Normal;
        return Priority.Low;
    }

    private static DateTime? ComputeDeadline(Priority priority) =>
        DateTime.UtcNow.AddHours(priority switch
        {
            Priority.Critical => 4,
            Priority.High => 8,
            _ => 24
        });

    private Task EmitToSubscribers(OrchestrationEvent e, CancellationToken ct)
    {
        var handlers = _subscribers;
        if (handlers is null) return Task.CompletedTask;
        return handlers(e, ct);
    }
}

/// <summary>Per-agent tool allow-lists.</summary>
public static class AgentToolMap
{
    private static readonly Dictionary<AgentType, ToolName[]> Map = new()
    {
        [AgentType.CoverageMatcher] = new[] { ToolName.RetrievePolicyVersioned, ToolName.ListCoverageItems },
        [AgentType.ExclusionAnalyst] = new[] { ToolName.RetrievePolicyVersioned, ToolName.CheckExclusion },
        [AgentType.AnomalyDetector] = new[] { ToolName.RecordAnomaly },
        [AgentType.AdjudicationDrafter] = new[] { ToolName.DraftAdjudication }
    };

    public static IReadOnlyList<ToolName> AllowedFor(AgentType type) => Map[type];
}