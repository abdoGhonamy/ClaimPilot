using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.Security.Claims;

using ClaimPilot.API.Auth;
using ClaimPilot.API.Dtos;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/review")]
[Authorize(Roles = "Adjuster,Supervisor,Director")]
public sealed class ReviewController : ControllerBase
{
    private readonly IApprovalQueueReader _queue;
    private readonly IApprovalService _service;

    public ReviewController(IApprovalQueueReader queue, IApprovalService service)
    {
        _queue = queue;
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApprovalItemView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApprovalItemView>>> Queue(
        [FromQuery] ApprovalStatus? status, [FromQuery] AssigneeRole? assigneeId, [FromQuery] Priority? priority,
        CancellationToken ct)
    {
        var items = await _queue.GetQueueAsync(new ReviewQueueFilter(status ?? ApprovalStatus.Pending, assigneeId, null, priority), ct);
        return Ok(items);
    }

    [HttpGet("{approvalItemId:guid}")]
    [ProducesResponseType<ApprovalItemDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApprovalItemDetail>> Get(Guid approvalItemId, CancellationToken ct)
    {
        var detail = await _queue.GetAsync(approvalItemId, ct);
        return detail is null ? NotFound() : Ok(detail);
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
}