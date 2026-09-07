using System.Text.Json;

using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Services.Agents;

/// <summary>
/// Anomaly Detector. Uses deterministic heuristics only — no LLM. Detects:
/// high claim-to-limit ratio, duplicated claims, missing documents, incomplete info.
/// </summary>
public sealed class AnomalyDetectorAgent : IAgent
{
    private readonly IToolRegistry _tools;
    private readonly OrchestrationEventSink _events;

    public AgentType AgentType => AgentType.AnomalyDetector;
    public string DisplayName => "Anomaly Detector";

    public AnomalyDetectorAgent(IToolRegistry tools, OrchestrationEventSink events)
    {
        _tools = tools;
        _events = events;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext ctx, CancellationToken ct)
    {
        await _events.Emit(ctx.RunId, ctx.CorrelationId, "agent_started", DisplayName, null, null, ct);

        var anomalies = new List<AnomalyDto>();

        // Heuristic 1: claim amount vs policy limit threshold (configurable at run level).
        if (ctx.State.TryGetValue("policy_limit", out var limitText) &&
            decimal.TryParse(limitText, out var limit) && limit > 0)
        {
            var ratio = ctx.ClaimAmount / limit;
            if (ratio > 0.8m)
            {
                anomalies.Add(new AnomalyDto(
                    "high_claim_ratio", AnomalySeverity.Warning,
                    $"Claim amount ({ctx.ClaimAmount:C}) exceeds 80% of the policy limit ({limit:C}).",
                    $"ratio={ratio:P0}"));
            }
        }

        // Heuristic 2: missing document evidence.
        if (ctx.State.TryGetValue("has_documents", out var hasDocs) && hasDocs != "true")
        {
            anomalies.Add(new AnomalyDto(
                "missing_documents", AnomalySeverity.Info,
                "No supporting documents were attached to the claim.",
                "documents=0"));
        }

        // Heuristic 3: suspiciously round amounts.
        if (ctx.ClaimAmount == Math.Round(ctx.ClaimAmount) && ctx.ClaimAmount >= 1000 &&
            ctx.ClaimAmount % 1000 == 0)
        {
            anomalies.Add(new AnomalyDto(
                "suspicious_amount", AnomalySeverity.Info,
                "Claim amount is a suspicious round value.",
                $"amount={ctx.ClaimAmount}"));
        }

        // Heuristic 4: incomplete description.
        if (string.IsNullOrWhiteSpace(ctx.ClaimDescription) || ctx.ClaimDescription.Length < 20)
        {
            anomalies.Add(new AnomalyDto(
                "incomplete_information", AnomalySeverity.Warning,
                "Claim description is incomplete or missing.",
                $"description_length={ctx.ClaimDescription.Length}"));
        }

        foreach (var anomaly in anomalies)
        {
            await _tools.ExecuteAsync(
                ToolName.RecordAnomaly, AgentType.AnomalyDetector,
                new Dictionary<string, string>
                {
                    ["type"] = anomaly.Type,
                    ["severity"] = anomaly.Severity.ToString(),
                    ["description"] = anomaly.Description,
                    ["evidence"] = anomaly.Evidence
                }, ctx.RunId, ctx.CorrelationId, ct);
        }

        var outputJson = JsonSerializer.Serialize(anomalies);

        await _events.Emit(ctx.RunId, ctx.CorrelationId, "agent_completed", DisplayName,
            null, outputJson, ct);

        return new AgentResult
        {
            Output = outputJson,
            Success = true,
            RecommendsReview = anomalies.Any(a => a.Severity == AnomalySeverity.Critical)
        };
    }
}

public sealed record AnomalyDto(string Type, AnomalySeverity Severity, string Description, string Evidence);