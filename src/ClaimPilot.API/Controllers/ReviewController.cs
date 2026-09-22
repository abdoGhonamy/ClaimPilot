using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

using System.Security.Claims;

using ClaimPilot.API.Auth;
using ClaimPilot.API.Dtos;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/review")]
[Authorize(Roles = "Adjuster,Supervisor,Director")]
public sealed class ReviewController : ControllerBase
{
    private readonly IApprovalQueueReader _queue;
    private readonly IApprovalService _service;
    private readonly ITraceService _trace;

    public ReviewController(IApprovalQueueReader queue, IApprovalService service, ITraceService trace)
    {
        _queue = queue;
        _service = service;
        _trace = trace;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApprovalItemView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApprovalItemView>>> Queue(
        [FromQuery] ApprovalStatus? status, [FromQuery] AssigneeRole? assigneeId, [FromQuery] Priority? priority,
        CancellationToken ct)
    {
        var items = await _queue.GetQueueAsync(new ReviewQueueFilter(status ?? ApprovalStatus.Pending, assigneeId, null, priority), ct);
        // A reviewer may only discover work assigned to one of their actual roles.
        // This is enforced server-side, not just hidden by the browser UI.
        return Ok(items.Where(CanView).ToList());
    }

    [HttpGet("{approvalItemId:guid}")]
    [ProducesResponseType<ApprovalItemDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApprovalItemDetail>> Get(Guid approvalItemId, CancellationToken ct)
    {
        var detail = await _queue.GetAsync(approvalItemId, ct);
        if (detail is null) return NotFound();
        return CanView(detail.AssignedTo) ? Ok(detail) : Forbid();
    }

    [HttpGet("{approvalItemId:guid}/explanation")]
    [ProducesResponseType<ReviewExplanationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewExplanationDto>> Explanation(Guid approvalItemId, CancellationToken ct)
    {
        var detail = await _queue.GetAsync(approvalItemId, ct);
        if (detail is null) return NotFound();
        if (!CanView(detail.AssignedTo)) return Forbid();

        var item = (await _queue.GetQueueAsync(new ReviewQueueFilter(null), ct)).FirstOrDefault(x => x.Id == approvalItemId);
        if (item?.RunId is not { } runId) return NotFound();
        var trace = await _trace.GetRunAsync(runId.ToString(), ct);

        var exclusions = trace.Where(row => row.EntityType == "ToolCall" && row.Action == "CheckExclusion")
            .Select(ParseExclusion).Where(row => row is not null).Cast<ReviewExclusionDto>().ToList();
        var anomalies = trace.Where(row => row.EntityType == "ToolCall" && row.Action == "RecordAnomaly")
            .Select(ParseAnomaly).Where(row => row is not null).Cast<ReviewAnomalyDto>().ToList();
        var computation = trace.LastOrDefault(row => row.EntityType == "Computation" && row.Action == "completed");
        var (steps, reason) = ParseComputation(computation?.After);

        return Ok(new ReviewExplanationDto(runId, exclusions, anomalies, steps, reason));
    }

    [HttpPost("{approvalItemId:guid}/approve")]
    [RequireApprovalAuthority(Auth.ApprovalAction.Approve)]
    [ProducesResponseType<ReviewActionResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewActionResult>> Approve(Guid approvalItemId, [FromBody] ReviewApproveBody request, CancellationToken ct)
        => Ok(await _service.ApproveAsync(approvalItemId, new ApproveRequest(ReviewerId(), request.Comment), ct));

    [HttpPost("{approvalItemId:guid}/reject")]
    [RequireApprovalAuthority(Auth.ApprovalAction.Reject)]
    public async Task<ActionResult<ReviewActionResult>> Reject(Guid approvalItemId, [FromBody] ReviewRejectBody request, CancellationToken ct)
        => Ok(await _service.RejectAsync(approvalItemId, new RejectRequest(ReviewerId(), request.Comment), ct));

    [HttpPost("{approvalItemId:guid}/edit")]
    [RequireApprovalAuthority(Auth.ApprovalAction.Edit)]
    public async Task<ActionResult<ReviewActionResult>> Edit(Guid approvalItemId, [FromBody] ReviewEditBody request, CancellationToken ct)
        => Ok(await _service.EditAsync(approvalItemId,
            new EditRequest(ReviewerId(), request.Comment, request.EditedDecisionJson, request.EditedAmount), ct));

    [HttpPost("{approvalItemId:guid}/re-review")]
    [Authorize(Roles = "Adjuster,Supervisor,Director")]
    public async Task<ActionResult<ReviewActionResult>> ReReview(Guid approvalItemId, [FromBody] ReviewReReviewBody request, CancellationToken ct)
        => Ok(await _service.ReReviewAsync(approvalItemId, new ReReviewRequest(ReviewerId(), request.Comment), ct));

    [HttpPost("{approvalItemId:guid}/assign")]
    [Authorize(Roles = "Supervisor,Director")]
    public async Task<ActionResult<ReviewActionResult>> Assign(Guid approvalItemId, [FromBody] ReviewAssignBody request, CancellationToken ct)
        => Ok(await _service.AssignAsync(approvalItemId, new AssignRequest(request.Assignee, ReviewerId(), request.Comment), ct));

    [HttpPost("{approvalItemId:guid}/escalate")]
    [Authorize(Roles = "Adjuster,Supervisor,Director")]
    public async Task<ActionResult<ReviewActionResult>> Escalate(Guid approvalItemId, [FromBody] ReviewEscalateBody request, CancellationToken ct)
        => Ok(await _service.EscalateAsync(approvalItemId,
            new EscalateRequest(ReviewerId(), ReviewerRoles(), request.Comment), ct));

    [HttpPost("{approvalItemId:guid}/priority")]
    [Authorize(Roles = "Supervisor,Director")]
    public async Task<ActionResult<ReviewActionResult>> OverridePriority(Guid approvalItemId, [FromBody] ReviewPriorityBody request, CancellationToken ct)
        => Ok(await _service.OverridePriorityAsync(approvalItemId,
            new PriorityOverrideRequest(ReviewerId(), request.NewPriority, request.Comment), ct));

    private string ReviewerId() => User.Identity?.Name ?? "anonymous";

    private IReadOnlyList<string> ReviewerRoles()
        => User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

    private bool CanView(ApprovalItemView item) => CanView(item.AssignedTo);

    private bool CanView(AssigneeRole? assignedTo)
    {
        // Unassigned items are not actionable until routed, so do not expose them
        // through a role-specific work queue.
        return assignedTo.HasValue && User.IsInRole(assignedTo.Value.ToString());
    }

    private static ReviewExclusionDto? ParseExclusion(TraceRecord row)
    {
        try
        {
            using var result = JsonDocument.Parse(row.After ?? "{}");
            return new ReviewExclusionDto(
                result.RootElement.TryGetProperty("Code", out var code) ? code.GetString() ?? "Unknown" : "Unknown",
                result.RootElement.TryGetProperty("Name", out var name) ? name.GetString() : null,
                result.RootElement.TryGetProperty("IsApplicable", out var applies) && applies.GetBoolean(),
                result.RootElement.TryGetProperty("Evidence", out var evidence) ? evidence.GetString() : null);
        }
        catch (JsonException) { return null; }
    }

    private static ReviewAnomalyDto? ParseAnomaly(TraceRecord row)
    {
        try
        {
            using var input = JsonDocument.Parse(row.Before ?? "{}");
            return new ReviewAnomalyDto(
                input.RootElement.TryGetProperty("type", out var type) ? type.GetString() ?? "Unknown" : "Unknown",
                input.RootElement.TryGetProperty("severity", out var severity) ? severity.GetString() ?? "Info" : "Info",
                input.RootElement.TryGetProperty("description", out var description) ? description.GetString() ?? "No description" : "No description",
                input.RootElement.TryGetProperty("evidence", out var evidence) ? evidence.GetString() : null);
        }
        catch (JsonException) { return null; }
    }

    private static (IReadOnlyList<ReviewComputationStepDto> Steps, string? Reason) ParseComputation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (Array.Empty<ReviewComputationStepDto>(), null);
        try
        {
            using var value = JsonDocument.Parse(json);
            var steps = value.RootElement.TryGetProperty("StepTrace", out var source)
                ? source.EnumerateArray().Select(step => new ReviewComputationStepDto(
                    step.TryGetProperty("Step", out var name) ? name.GetString() ?? "Step" : "Step",
                    step.TryGetProperty("Description", out var description) ? description.GetString() ?? "" : "",
                    step.TryGetProperty("Amount", out var amount) && amount.TryGetDecimal(out var number) ? number : null,
                    step.TryGetProperty("Detail", out var detail) ? detail.GetString() : null)).ToList()
                : new List<ReviewComputationStepDto>();
            return (steps, value.RootElement.TryGetProperty("InsufficiencyReason", out var reason) ? reason.GetString() : null);
        }
        catch (JsonException) { return (Array.Empty<ReviewComputationStepDto>(), null); }
    }
}
