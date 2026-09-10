using System.Threading.Channels;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Dtos;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/claims")]
[Authorize(Roles = "Adjuster,Supervisor,Viewer")]
public sealed class ClaimsController : ControllerBase
{
    private readonly IClaimRepository _claims;
    private readonly IRunTraceViewBuilder _traceView;

    public ClaimsController(IClaimRepository claims, IRunTraceViewBuilder traceView)
    {
        _claims = claims;
        _traceView = traceView;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClaimDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClaimDto>>> List(CancellationToken ct)
    {
        var all = await _claims.GetAllAsync(ct);
        return Ok(all.Select(c => new ClaimDto(c.Id, c.ClaimNumber, c.PolicyNumber, c.IncidentDate,
            c.ClaimAmount, c.Description, c.Status, c.CreatedAt)).ToList());
    }

    [HttpGet("{claimId:guid}")]
    [ProducesResponseType<ClaimDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClaimDto>> Get(Guid claimId, CancellationToken ct)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct)
            ?? throw new DomainException($"Claim {claimId} not found.");
        return Ok(new ClaimDto(claim.Id, claim.ClaimNumber, claim.PolicyNumber, claim.IncidentDate,
            claim.ClaimAmount, claim.Description, claim.Status, claim.CreatedAt));
    }

    [HttpGet("{claimId:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<RunDto>>> Runs(Guid claimId, CancellationToken ct)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct);
        if (claim is null) return NotFound();

        return Ok(claim.Runs.OrderByDescending(r => r.CreatedAt).Select(r => new RunDto(
            r.Id, r.ClaimId, claim.ClaimNumber, claim.PolicyNumber, null,
            r.Status, r.Degraded, r.ApprovalItems.Any(),
            r.ApprovalItems.LastOrDefault()?.Id, r.FinalDecision?.ApprovedAmount,
            r.FinalDecision?.Rationale, r.Anomalies.Select(a => a.Description).ToList(), r.CreatedAt)).ToList());
    }

    [HttpGet("runs/{runId:guid}/trace")]
    [ProducesResponseType<TraceViewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TraceViewResponse>> Trace(Guid runId, CancellationToken ct)
    {
        var view = await _traceView.BuildAsync(runId.ToString(), ct);
        if (view is null) return NotFound();

        return Ok(new TraceViewResponse(view.RunId, view.ClaimNumber, view.PolicyNumber, view.PolicyVersion,
            view.EffectiveDate, view.Status, view.Degraded, view.FailReason, view.Agents, view.ToolCalls,
            view.RetrievedChunks, view.ComputationSteps, view.Anomalies,
            view.Usage.Select(u => new UsageDto(u.Provider, u.Model, u.InputTokens, u.OutputTokens, u.EstimatedCostUsd)).ToList(),
            view.EstimatedCost, view.ApprovalHistory, view.FinalResult));
    }

    /// <summary>
    /// Runs the supervisor workflow and streams a live SSE trace of
    /// orchestrator events (agent activity, tool calls, policy version selection)
    /// followed by the final run result. Requires Adjuster or Supervisor.
    /// </summary>
    [HttpPost("{claimId:guid}/adjudicate")]
    [Authorize(Roles = "Adjuster,Supervisor")]
    public async Task Adjudicate(Guid claimId, [FromQuery] string? correlationId, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var orchestrator = HttpContext.RequestServices.GetRequiredService<IClaimsOrchestrator>();
        var channel = Channel.CreateUnbounded<string>();

        OrchestrationEventHandler handler = (e, token) =>
        {
            channel.Writer.TryWrite(SerializeEvent("event", e.EventType, e.RunId, e.Agent, e.Tool, e.Payload));
            return Task.CompletedTask;
        };
        orchestrator.EventRaised += handler;

        try
        {
            var runTask = Task.Run(() => orchestrator.RunAsync(claimId, correlationId, ct), ct);

            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var message))
                {
                    await Response.WriteAsync(message, ct);
                    await Response.Body.FlushAsync(ct);
                }
            }

            var run = await runTask;
            await Response.WriteAsync(SerializeEvent("run_complete", run.Status, run.RunId, null, null,
                System.Text.Json.JsonSerializer.Serialize(run)), ct);
        }
        catch (Exception ex)
        {
            await Response.WriteAsync(SerializeEvent("error", "error", claimId, null, null,
                System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message })), ct);
        }
        finally
        {
            orchestrator.EventRaised -= handler;
            channel.Writer.TryComplete();
        }
    }

    private static string SerializeEvent(
        string eventName, string? eventType, Guid runId, string? agent, string? tool, string? payload) =>
        $"event: {eventName}\ndata: {System.Text.Json.JsonSerializer.Serialize(new
        {
            event_type = eventType,
            run_id = runId,
            agent,
            tool,
            payload
        })}\n\n";
}