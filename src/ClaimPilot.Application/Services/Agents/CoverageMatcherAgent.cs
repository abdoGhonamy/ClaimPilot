using System.Text.Json;

using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.Application.Services.Agents;

/// <summary>
/// Coverage Matcher agent. Resolves the exact policy version for the incident
/// date (deterministic), lists coverage items for that version, and uses the
/// LLM strictly for language understanding — never for numbers.
/// </summary>
public sealed class CoverageMatcherAgent : IAgent
{
    private readonly IToolRegistry _tools;
    private readonly ILLMProvider _llm;
    private readonly OrchestrationEventSink _events;

    public AgentType AgentType => AgentType.CoverageMatcher;
    public string DisplayName => "Coverage Matcher";

    public CoverageMatcherAgent(IToolRegistry tools, ILLMProvider llm, OrchestrationEventSink events)
    {
        _tools = tools;
        _llm = llm;
        _events = events;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext ctx, CancellationToken ct)
    {
        await _events.Emit(ctx.RunId, ctx.CorrelationId, "agent_started", DisplayName, null, null, ct);

        var toolCalls = new List<ToolCallRecord>();

        var retrieveCall = await _tools.ExecuteAsync(
            ToolName.RetrievePolicyVersioned, AgentType.CoverageMatcher,
            new Dictionary<string, string>
            {
                ["policy_number"] = ctx.PolicyNumber,
                ["incident_date"] = ctx.IncidentDate.ToString("yyyy-MM-dd")
            }, ctx.RunId, ctx.CorrelationId, ct);
        toolCalls.Add(retrieveCall);

        if (!retrieveCall.Succeeded)
            throw new DomainException($"Coverage Matcher failed: {retrieveCall.Error}");

        var match = JsonSerializer.Deserialize<PolicyMatchResult>(retrieveCall.OutputJson)
            ?? throw new DomainException("Coverage Matcher could not parse policy match.");

        var listCall = await _tools.ExecuteAsync(
            ToolName.ListCoverageItems, AgentType.CoverageMatcher,
            new Dictionary<string, string> { ["version_id"] = match.VersionId.ToString() },
            ctx.RunId, ctx.CorrelationId, ct);
        toolCalls.Add(listCall);

        if (!listCall.Succeeded)
            throw new DomainException($"Coverage Matcher could not list coverage: {listCall.Error}");

        var coverageItems = JsonSerializer.Deserialize<List<CoverageLine>>(listCall.OutputJson)
            ?? new List<CoverageLine>();

        var output = new
        {
            version_id = match.VersionId,
            version = match.Version,
            effective_date = match.EffectiveDate,
            coverage_items = coverageItems,
            citation = new
            {
                policy_id = match.PolicyId,
                version = match.Version,
                section = nameof(CoverageLine),
                clause = match.CoverageItems.FirstOrDefault()?.Code ?? "all"
            }
        };

        await _events.Emit(ctx.RunId, ctx.CorrelationId, "agent_completed", DisplayName,
            null, JsonSerializer.Serialize(output), ct);

        return new AgentResult
        {
            Output = JsonSerializer.Serialize(output),
            Success = true,
            ToolCalls = toolCalls,
            RecommendsReview = false
        };
    }
}

/// <summary>Small event sink that forwards orchestration events to subscribers.</summary>
public sealed class OrchestrationEventSink
{
    public event OrchestrationEventHandler? Raised;

    public async Task Emit(Guid runId, string? correlationId, string eventType, string? agent,
        string? tool, string? payload, CancellationToken ct)
    {
        var handlers = Raised;
        if (handlers is null) return;

        var evt = new OrchestrationEvent
        {
            EventType = eventType,
            RunId = runId,
            CorrelationId = correlationId,
            OccurredAt = DateTime.UtcNow,
            Agent = agent,
            Tool = tool,
            Payload = payload
        };

        foreach (var handler in handlers.GetInvocationList().OfType<OrchestrationEventHandler>())
        {
            await handler(evt, ct);
        }
    }
}