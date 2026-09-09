using System.Text.Json;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Builds the full observability view for a run: policy version, agents, tool
/// calls, retrieved chunks, computation steps, anomalies, LLM usage/cost,
/// approval history, and the final result.
/// </summary>
public sealed class RunTraceViewBuilder : IRunTraceViewBuilder
{
    private readonly ITraceService _trace;
    private readonly IClaimRepository _claims;
    private readonly IUsageTracker _usage;
    private readonly IApprovalRepository _approvals;

    public RunTraceViewBuilder(
        ITraceService trace,
        IClaimRepository claims,
        IUsageTracker usage,
        IApprovalRepository approvals)
    {
        _trace = trace;
        _claims = claims;
        _usage = usage;
        _approvals = approvals;
    }

    public async Task<RunTraceView> BuildAsync(string runId, CancellationToken ct)
    {
        if (!Guid.TryParse(runId, out var runGuid))
            return new RunTraceView(runId, string.Empty, string.Empty, null, null, "invalid", false,
                "Invalid run id.", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<UsageRecord>(), null,
                Array.Empty<string>(), null);

        var run = await _claims.GetRunAsync(runGuid, ct);
        var trace = await _trace.GetRunAsync(runId, ct);
        var usage = await _usage.GetForRunAsync(runId, ct);
        var cost = await _usage.EstimateRunCostAsync(runId, ct);

        var claim = await _claims.GetByIdAsync(run?.ClaimId ?? Guid.Empty, ct);

        var agentSteps = trace
            .Where(t => t.EntityType == "AgentRun")
            .Select(t => t.Message ?? t.Action)
            .ToList();

        var toolCalls = trace
            .Where(t => t.EntityType == "ToolCall")
            .Select(t => $"{t.Action} in={Shorten(t.Before)} out={Shorten(t.After)}")
            .ToList();

        var retrievedChunks = trace
            .Where(t => t.EntityType == "Retrieval" || t.Action.Contains("retrieve", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Message ?? t.Action)
            .ToList();

        var computationSteps = trace
            .Where(t => t.EntityType == "Computation")
            .Select(t => t.After ?? t.Message ?? string.Empty)
            .ToList();

        var anomalies = trace
            .Where(t => t.EntityType == "Anomaly" || t.EntityType == "AgentRun" && t.Message?.Contains("Anomaly") == true)
            .Select(t => t.Message ?? t.Action)
            .ToList();

        var approvalRows = new List<string>();
        if (run is not null && run.ApprovalItems.Count == 0 && Guid.Empty != run.Id)
        {
            var approvals = await _approvals.QueryAsync(null, null, null, ct);
            foreach (var item in approvals.Where(i => i.RunId == run.Id))
            {
                approvalRows.Add($"{item.Status} created {item.CreatedAt:O} reviewed {item.ReviewedAt:O}");
            }
        }

        var finalResult = run?.FinalDecision != null
            ? JsonSerializer.Serialize(run.FinalDecision)
            : null;

        return new RunTraceView(
            runId,
            claim?.ClaimNumber ?? string.Empty,
            claim?.PolicyNumber ?? string.Empty,
            ExtractVersion(trace),
            ExtractEffectiveDate(trace),
            run?.Status.ToString() ?? "unknown",
            run?.Degraded == true,
            run?.FailReason,
            agentSteps,
            toolCalls,
            retrievedChunks,
            computationSteps,
            anomalies,
            usage,
            cost,
            approvalRows,
            finalResult);
    }

    private static int? ExtractVersion(IReadOnlyList<TraceRecord> trace)
    {
        var row = trace.FirstOrDefault(t => t.EntityType == "PolicyVersion");
        if (row?.Message?.Contains("Version", StringComparison.OrdinalIgnoreCase) == true)
        {
            var start = row.Message.IndexOf("Version", StringComparison.OrdinalIgnoreCase) + 8;
            var end = row.Message.IndexOf(" ", start);
            if (start > 0 && end > start && int.TryParse(row.Message[start..end], out var v)) return v;
        }
        return null;
    }

    private static DateTime? ExtractEffectiveDate(IReadOnlyList<TraceRecord> trace)
    {
        var row = trace.FirstOrDefault(t => t.EntityType == "PolicyVersion");
        if (row?.Message?.Contains("effective", StringComparison.OrdinalIgnoreCase) == true)
        {
            var mark = "effective ";
            var idx = row.Message.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx + mark.Length < row.Message.Length)
            {
                var token = row.Message[(idx + mark.Length)..].Split(' ').First();
                if (DateTime.TryParse(token, out var d)) return d;
            }
        }
        return null;
    }

    private static string Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= 80 ? value : value[..80] + "…";
    }
}