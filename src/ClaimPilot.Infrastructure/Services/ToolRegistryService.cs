using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Services;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;
using ClaimPilot.Infrastructure.Data;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Central tool execution registry with per-agent allow-lists, a gated write
/// (draft_adjudication), and audit requirements (record_anomaly).
/// </summary>
public sealed class ToolRegistryService : IToolRegistry
{
    private readonly IPolicyRepository _policies;
    private readonly IClaimRepository _claims;
    private readonly IAuditService _audit;
    private readonly IToolTraceWriter _trace;
    private readonly AppDbContext _db;
    private readonly ILogger<ToolRegistryService> _logger;

    public ToolRegistryService(
        IPolicyRepository policies,
        IClaimRepository claims,
        IAuditService audit,
        IToolTraceWriter trace,
        AppDbContext db,
        ILogger<ToolRegistryService> logger)
    {
        _policies = policies;
        _claims = claims;
        _audit = audit;
        _trace = trace;
        _db = db;
        _logger = logger;
    }

    public IReadOnlyList<ToolDefinition> Definitions { get; } = new[]
    {
        new ToolDefinition { Name = ToolName.RetrievePolicyVersioned, Description = "Resolve the exact policy version for an incident date and return coverage.", ParameterSchema = new[] { "policy_number", "incident_date" } },
        new ToolDefinition { Name = ToolName.ListCoverageItems, Description = "List coverage items for a policy version.", ParameterSchema = new[] { "version_id" } },
        new ToolDefinition { Name = ToolName.CheckExclusion, Description = "Check whether an exclusion applies to a claim description. Requires evidence.", ParameterSchema = new[] { "policy_number", "version", "exclusion_code", "claim_description" } },
        new ToolDefinition { Name = ToolName.DraftAdjudication, Description = "Write a draft decision. GATED: never final without human approval.", IsWrite = true, IsGatedWrite = true, ParameterSchema = new[] { "run_id", "decision_json" } },
        new ToolDefinition { Name = ToolName.RecordAnomaly, Description = "Record an anomaly for a run. Audited.", IsWrite = true, RequiresAudit = true, ParameterSchema = new[] { "type", "severity", "description", "evidence" } }
    };

    public ToolDefinition GetDefinition(ToolName name)
        => Definitions.First(d => d.Name == name);

    public async Task<ToolCallRecord> ExecuteAsync(
        ToolName name, AgentType agentType, IReadOnlyDictionary<string, string> parameters,
        Guid runId, string? correlationId, CancellationToken ct)
    {
        var definition = GetDefinition(name);

        // Enforce per-agent allow-list.
        if (!AgentToolMap.AllowedFor(agentType).Contains(name))
        {
            return Failure(name, parameters, runId, $"Tool '{name}' is not allowed for agent '{agentType}'.");
        }

        var started = DateTime.UtcNow;
        var input = JsonSerializer.Serialize(parameters);

        try
        {
            string output;
            switch (name)
            {
                case ToolName.RetrievePolicyVersioned:
                    output = JsonSerializer.Serialize(await RetrievePolicyVersionedAsync(parameters, runId, ct));
                    break;
                case ToolName.ListCoverageItems:
                    output = await ListCoverageItemsAsync(parameters, ct);
                    break;
                case ToolName.CheckExclusion:
                    output = await CheckExclusionAsync(parameters, ct);
                    break;
                case ToolName.DraftAdjudication:
                    output = await DraftAdjudicationAsync(parameters, runId, ct);
                    break;
                case ToolName.RecordAnomaly:
                    output = await RecordAnomalyAsync(parameters, runId, ct);
                    break;
                default:
                    throw new DomainException($"Unknown tool '{name}'.");
            }

            await _trace.WriteToolAsync(runId.ToString(), name.ToString(), input, output, correlationId);
            return new ToolCallRecord
            {
                Id = Guid.NewGuid(),
                Tool = name,
                InputJson = input,
                OutputJson = output,
                Succeeded = true,
                StartedAt = started,
                EndedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} failed for run {RunId}", name, runId);
            await _trace.WriteToolAsync(runId.ToString(), name.ToString(), input, $"error: {ex.Message}", correlationId);
            return Failure(name, parameters, runId, ex.Message);
        }
    }

    private async Task<PolicyMatchResult> RetrievePolicyVersionedAsync(
        IReadOnlyDictionary<string, string> p, Guid runId, CancellationToken ct)
    {
        var policyNumber = Get(p, "policy_number");
        var incidentStr = Get(p, "incident_date");

        if (string.IsNullOrWhiteSpace(policyNumber) || !DateTime.TryParse(incidentStr, out var incidentDateUnspecified))
            throw new DomainException("retrieve_policy_versioned requires policy_number and incident_date.");
        var incidentDate = DateTime.SpecifyKind(incidentDateUnspecified, DateTimeKind.Utc);

        var policy = await _policies.GetByPolicyNumberAsync(policyNumber, ct)
            ?? throw new DomainException($"Policy '{policyNumber}' not found.");
        var version = await _policies.GetApplicableVersionAsync(policy.Id, incidentDate, ct)
            ?? throw new PolicyVersionNotFoundException(policyNumber, incidentDate);

        var coverage = await _policies.GetCoverageItemsAsync(version.Id, ct);
        var exclusions = (await _policies.GetExclusionsAsync(version.Id, ct))
            .Select(e => new PolicyExclusionLine(e.Code, e.Name, e.Description ?? string.Empty)).ToList();

        var result = new PolicyMatchResult(policy.Id, version.Id, version.Version, version.EffectiveDate,
            coverage.Select(c => new CoverageLine(c.Code, c.Name, c.Amount,
                coverage.FirstOrDefault(x => x.Type == CoverageType.Deductible)?.Amount,
                c.PercentageRate, c.Description ?? string.Empty)).ToList(),
            exclusions);
        _logger.LogInformation("RetrievePolicyVersioned returned for run {RunId}: {Json}",
            runId, JsonSerializer.Serialize(result));
        return result;
    }

    private async Task<string> ListCoverageItemsAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!Guid.TryParse(Get(p, "version_id"), out var versionId))
            throw new DomainException("list_coverage_items requires version_id.");

        var coverage = await _policies.GetCoverageItemsAsync(versionId, ct);
        return JsonSerializer.Serialize(coverage.Select(c => new CoverageLine(
            c.Code, c.Name, c.Amount, c.Type == CoverageType.Deductible ? c.Amount : null,
            c.PercentageRate, c.Description ?? string.Empty)).ToList());
    }

    private async Task<string> CheckExclusionAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        var policyNumber = Get(p, "policy_number");
        var versionText = Get(p, "version");
        var code = Get(p, "exclusion_code");
        var description = Get(p, "claim_description") ?? string.Empty;

        var policy = await _policies.GetByPolicyNumberAsync(policyNumber, ct)
            ?? throw new DomainException($"Policy '{policyNumber}' not found.");
        if (!int.TryParse(versionText, out var versionNo))
            throw new DomainException("check_exclusion requires a numeric version.");

        var version = await _policies.GetVersionAsync(policy.Id, versionNo, ct)
            ?? throw new DomainException($"Version {versionNo} of {policyNumber} not found.");
        var exclusions = await _policies.GetExclusionsAsync(version.Id, ct);
        var exclusion = exclusions.FirstOrDefault(e => e.Code == code);

        if (exclusion is null)
            return JsonSerializer.Serialize(new ExclusionCheckResult(false, code, null, "Exclusion not found for this version."));

        // Deterministic keyword applicability check; evidence is always the exclusion text.
        var isApplicable = IsApplicable(exclusion, description, out var keywords, out var matched);
        _logger.LogInformation(
            "Exclusion {Code}: text='{Text}', claim='{Claim}', keywords=[{Keywords}], matched=[{Matched}], result={Result}",
            exclusion.Code, exclusion.Description, description, string.Join(",", keywords), string.Join(",", matched), isApplicable);
        return JsonSerializer.Serialize(new ExclusionCheckResult(isApplicable, exclusion.Code, exclusion.Name,
            isApplicable ? $"Exclusion '{exclusion.Code}' ({exclusion.Name}) text: {exclusion.Description}" : "No matching keywords in claim description."));
    }

    private static readonly HashSet<string> ExclusionStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "coverage", "apply", "applies", "policy", "insured", "shall", "this", "that",
        "with", "from", "loss", "damage", "caused", "directly", "indirectly", "result",
        "resulting", "occurring", "under", "which", "while", "used", "any", "all", "not"
    };

    private static bool IsApplicable(
        Exclusion exclusion,
        string description,
        out IReadOnlyList<string> keywords,
        out IReadOnlyList<string> matched)
    {
        keywords = Array.Empty<string>();
        matched = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(exclusion.Description)) return false;

        keywords = exclusion.Description
            .ToLowerInvariant()
            .Split(new[] { ' ', ',', ';', '.', '(', ')', '\n', '\r', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length >= 4)
            .Where(w => !ExclusionStopwords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        matched = keywords
            .Where(keyword => description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matched.Count >= 1;
    }

    /// <summary>
    /// Gated write: creates a pending draft decision. It NEVER produces a final
    /// decision. Human approval of the corresponding approval item is required
    /// before anything is issued.
    /// </summary>
    private async Task<string> DraftAdjudicationAsync(IReadOnlyDictionary<string, string> p, Guid runId, CancellationToken ct)
    {
        var run = await _claims.GetRunAsync(runId, ct)
            ?? throw new DomainException($"Run {runId} not found.");

        var decisionJson = Get(p, "decision_json");
        if (string.IsNullOrWhiteSpace(decisionJson))
            throw new DomainException("draft_adjudication requires decision_json.");

        // The gated write: we persist only a numbered draft, never a final decision.
        var decision = new Decision
        {
            AdjudicationRunId = run.Id,
            AdjudicationRun = run,
            DecisionType = ExtractType(decisionJson),
            ApprovedAmount = ExtractAmount(decisionJson),
            Rationale = decisionJson,
            IsFinal = false
        };

        var db = await SaveDraftAsync(decision, ct);
        await _audit.RecordAsync(new AuditLogEntry(
            "Decision", db.Id, "DraftCreated", ActorId: "system",
            After: decisionJson, RunId: runId.ToString()), ct);

        return JsonSerializer.Serialize(new DraftAdjudicationResult(true,
            $"Draft decision saved but NOT final. Awaiting human approval. draft_id={db.Id}", null));
    }

    private async Task<Decision> SaveDraftAsync(Decision decision, CancellationToken ct)
    {
        _db.Decisions.Add(decision);
        AttachIfDetached(decision.AdjudicationRun);
        await _db.SaveChangesAsync(ct);
        return decision;
    }

    private async Task<Anomaly> SaveAnomalyAsync(Anomaly anomaly, CancellationToken ct)
    {
        _db.Anomalies.Add(anomaly);
        AttachIfDetached(anomaly.AdjudicationRun);
        await _db.SaveChangesAsync(ct);
        return anomaly;
    }

    private void AttachIfDetached(AdjudicationRun run)
    {
        if (run is not null && _db.Entry(run).State == EntityState.Detached)
            _db.Attach(run);
    }

    private async Task<string> RecordAnomalyAsync(IReadOnlyDictionary<string, string> p, Guid runId, CancellationToken ct)
    {
        var type = Get(p, "type");
        var severityText = Get(p, "severity");
        var description = Get(p, "description");

        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(description))
            throw new DomainException("record_anomaly requires type, description and severity.");

        Enum.TryParse<AnomalySeverity>(severityText, true, out var severity);

        var run = await _claims.GetRunAsync(runId, ct);
        var db = await SaveAnomalyAsync(new Anomaly
        {
            AdjudicationRunId = runId,
            AdjudicationRun = run!,
            Type = type,
            Severity = severity,
            Description = description,
            Evidence = Get(p, "evidence")
        }, ct);

        return JsonSerializer.Serialize(new { recorded = true, anomaly_id = db.Id });
    }

    private static decimal? ExtractAmount(string decisionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(decisionJson);
            if (doc.RootElement.TryGetProperty("proposed_amount", out var a) && a.ValueKind == JsonValueKind.Number)
                return a.GetDecimal();
        }
        catch (JsonException) { }
        return null;
    }

    private static DecisionType ExtractType(string decisionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(decisionJson);
            if (doc.RootElement.TryGetProperty("decision", out var d))
            {
                if (d.GetString() == "Reject") return DecisionType.Reject;
                if (d.GetString() == "InsufficientInformation") return DecisionType.InsufficientInformation;
            }
        }
        catch (JsonException) { }
        return DecisionType.Approve;
    }

    private static string Get(IReadOnlyDictionary<string, string> parameters, string key)
        => parameters.TryGetValue(key, out var value) ? value : string.Empty;

    private static ToolCallRecord Failure(ToolName name, IReadOnlyDictionary<string, string> parameters,
        Guid runId, string message) =>
        new()
        {
            Id = Guid.NewGuid(),
            Tool = name,
            InputJson = JsonSerializer.Serialize(parameters),
            OutputJson = $"error: {message}",
            Succeeded = false,
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            Error = message
        };
}

/// <summary>Write access required by tools that persist gated writes.</summary>
public interface IToolTraceWriter
{
    Task WriteToolAsync(string runId, string tool, string input, string output, string? correlationId, CancellationToken ct = default);
}
