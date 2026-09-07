using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.ValueObjects;

namespace ClaimPilot.Application.Interfaces.Orchestration;

/// <summary>Contract every agent must satisfy.</summary>
public interface IAgent
{
    AgentType AgentType { get; }
    string DisplayName { get; }

    Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct);
}

public sealed record AgentContext
{
    public required Guid RunId { get; init; }
    public required Guid ClaimId { get; init; }
    public required string ClaimNumber { get; init; }
    public required string PolicyNumber { get; init; }
    public required DateTime IncidentDate { get; init; }
    public required decimal ClaimAmount { get; init; }
    public required string ClaimDescription { get; init; }
    public required IReadOnlyList<ToolName> AllowedTools { get; init; }
    public int MaxIterations { get; init; } = 5;
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, string> State { get; init; } = new Dictionary<string, string>();
}

public enum ToolName
{
    RetrievePolicyVersioned,
    ListCoverageItems,
    CheckExclusion,
    DraftAdjudication,
    RecordAnomaly
}

public sealed record ToolParam(string Name, string? Value);

public sealed record ToolCallRequest(ToolName Tool, IReadOnlyList<ToolParam> Parameters);

/// <summary>Capabilities an agent tool may execute.</summary>
public sealed record ToolDefinition
{
    public required ToolName Name { get; init; }
    public required string Description { get; init; }
    public bool IsWrite { get; init; }
    public bool IsGatedWrite { get; init; }
    public bool RequiresAudit { get; init; }
    public IReadOnlyList<string> ParameterSchema { get; init; } = Array.Empty<string>();
}

public sealed record AgentResult
{
    public required string Output { get; init; }
    public bool RecommendsReview { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = Array.Empty<Citation>();
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = Array.Empty<ToolCallRecord>();
    public bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>A record of every executed (or attempted) tool call for the trace.</summary>
public sealed record ToolCallRecord
{
    public required Guid Id { get; init; }
    public required ToolName Tool { get; init; }
    public required string InputJson { get; init; }
    public required string OutputJson { get; init; }
    public bool Succeeded { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public string? Error { get; init; }
}