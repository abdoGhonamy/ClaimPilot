using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Services;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.Tests;

public class ApprovalServiceStateMachineTests
{
    private readonly FakeApprovalRepository _repository = new();
    private readonly FakeAuditService _audit = new();
    private readonly ApprovalService _service;

    public ApprovalServiceStateMachineTests()
    {
        _service = new ApprovalService(_repository, _audit, new FakeClaimRepository(), NullLogger<ApprovalService>.Instance);
    }

    private ApprovalItem NewPendingItem(Guid? id = null, ApprovalStatus status = ApprovalStatus.Pending)
    {
        var item = new ApprovalItem
        {
            Id = id ?? Guid.NewGuid(),
            ClaimId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            Status = status,
            Priority = Priority.Normal,
            Title = "Decision required — CLAIM-2023-001"
        };
        _repository.AddSeed(item);
        return item;
    }

    [Fact]
    public async Task Pending_CanBeApproved_WithHistoryAndAudit()
    {
        var item = NewPendingItem();
        var reviewer = "supervisor";

        var result = await _service.ApproveAsync(item.Id, new ApproveRequest(reviewer, "looks good"), CancellationToken.None);

        result.NewStatus.Should().Be(ApprovalStatus.Approved);
        item.Status.Should().Be(ApprovalStatus.Approved);
        item.ReviewedAt.Should().NotBeNull();
        item.History.Should().ContainSingle(h => h.Action == ApprovalAction.Approved && h.ReviewerId == reviewer);
        _audit.Entries.Should().ContainSingle(e => e.Action == ApprovalAction.Approved.ToString());
    }

    [Fact]
    public async Task Reject_WithoutComment_Throws()
    {
        var item = NewPendingItem();

        var act = async () => await _service.RejectAsync(item.Id, new RejectRequest("supervisor", string.Empty), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidStateTransitionException>();
        item.Status.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task Reject_WithComment_TransitionsToRejected()
    {
        var item = NewPendingItem();

        var result = await _service.RejectAsync(item.Id, new RejectRequest("supervisor", "not covered"), CancellationToken.None);

        result.NewStatus.Should().Be(ApprovalStatus.Rejected);
        item.Status.Should().Be(ApprovalStatus.Rejected);
    }

    [Fact]
    public async Task AlreadyApproved_CannotBeApprovedAgain()
    {
        var item = NewPendingItem(status: ApprovalStatus.Approved);

        var act = async () => await _service.ApproveAsync(item.Id, new ApproveRequest("supervisor", "again"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidStateTransitionException>();
    }

    [Fact]
    public async Task Edit_ReturnsToPending_ForReReview()
    {
        var item = NewPendingItem();

        var result = await _service.EditAsync(item.Id,
            new EditRequest("supervisor", "adjust amount", "{\"amount\":4000}", 4000m), CancellationToken.None);

        result.NewStatus.Should().Be(ApprovalStatus.Pending);
        item.Status.Should().Be(ApprovalStatus.Edited);
        item.History.Should().ContainSingle(h => h.Action == ApprovalAction.Edited && h.EditDiff != null);
    }

    [Fact]
    public async Task ReReview_LoopsEditedForward_ToPending()
    {
        var item = NewPendingItem(status: ApprovalStatus.Edited);

        var result = await _service.ReReviewAsync(item.Id, new ReReviewRequest("supervisor", "re-check"), CancellationToken.None);

        result.NewStatus.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task Escalate_MarksEscalated_AndForwardsToDirector()
    {
        var item = NewPendingItem();

        var result = await _service.EscalateAsync(item.Id, new EscalateRequest("adjuster", "need director"), CancellationToken.None);

        result.NewStatus.Should().Be(ApprovalStatus.Escalated);
        item.AssignedTo.Should().Be("director");
    }

    [Fact]
    public async Task MissingItem_Throws()
    {
        var act = async () => await _service.ApproveAsync(Guid.NewGuid(), new ApproveRequest("supervisor", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidStateTransitionException>();
    }
}