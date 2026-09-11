using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.ValueObjects;

namespace ClaimPilot.Application.Interfaces.Orchestration;

/// <summary>
/// Central gatekeeper for tool execution. Enforces:
///  - per-agent tool allow-lists
///  - gated write tools (draft_adjudication requires human approval)
///  - audit requirements (record_anomaly is audited)
/// </summary>
public interface IToolRegistry
{
    IReadOnlyList<ToolDefinition> Definitions { get; }
    ToolDefinition GetDefinition(ToolName name);

    Task<ToolCallRecord> ExecuteAsync(
        ToolName name,
        AgentType agentType,
        IReadOnlyDictionary<string, string> parameters,
        Guid runId,
        string? correlationId,
        CancellationToken ct);
}

/// <summary>Structured outputs returned by the tools (used by agents and trace).</summary>
public sealed record PolicyMatchResult(
    Guid PolicyId,
    Guid VersionId,
    int Version,
    DateTime EffectiveDate,
    IReadOnlyList<CoverageLine> CoverageItems,
    IReadOnlyList<PolicyExclusionLine>? Exclusions = null);

public sealed record CoverageLine(string Code, string Name, decimal? Limit, decimal? Deductible, decimal? Coinsurance, string Description);

public sealed record PolicyExclusionLine(string Code, string Name, string Description);

public sealed record ExclusionCheckResult(bool IsApplicable, string? Code, string? Name, string? Evidence);

public sealed record DraftResult(
    string DecisionType,
    decimal? ProposedAmount,
    string Rationale,
    string? CitationsJson,
    IReadOnlyList<Citation> Citations);

/// <summary>Gated write: carries a flag that is only cleared by the orchestrator after human approval.</summary>
public sealed record DraftAdjudicationResult(bool Written, string? PendingApprovalItemId, string? Error);