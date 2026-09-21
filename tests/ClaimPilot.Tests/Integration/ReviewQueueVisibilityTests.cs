using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Controllers;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Tests.Integration;

public sealed class ReviewQueueVisibilityTests
{
    [Fact]
    public async Task Adjuster_OnlyReceivesAdjusterAssignedItems()
    {
        var reader = new FakeApprovalQueueReader();
        reader.AddView(View(AssigneeRole.Adjuster));
        reader.AddView(View(AssigneeRole.Supervisor));
        reader.AddView(View(AssigneeRole.Director));
        var controller = Controller(reader, "Adjuster");

        var result = await controller.Queue(ApprovalStatus.Pending, null, null, CancellationToken.None);

        var items = ((OkObjectResult)result.Result!).Value.Should().BeAssignableTo<IReadOnlyList<ApprovalItemView>>().Subject;
        items.Should().ContainSingle();
        items[0].AssignedTo.Should().Be(AssigneeRole.Adjuster);
    }

    [Fact]
    public async Task Supervisor_ReceivesSupervisorAndInheritedAdjusterItems()
    {
        var reader = new FakeApprovalQueueReader();
        reader.AddView(View(AssigneeRole.Adjuster));
        reader.AddView(View(AssigneeRole.Supervisor));
        reader.AddView(View(AssigneeRole.Director));
        var controller = Controller(reader, "Adjuster", "Supervisor");

        var result = await controller.Queue(ApprovalStatus.Pending, null, null, CancellationToken.None);

        var items = ((OkObjectResult)result.Result!).Value.Should().BeAssignableTo<IReadOnlyList<ApprovalItemView>>().Subject;
        items.Select(x => x.AssignedTo).Should().BeEquivalentTo(new[] { AssigneeRole.Adjuster, AssigneeRole.Supervisor });
    }

    private static ReviewController Controller(FakeApprovalQueueReader reader, params string[] roles)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var role in roles) identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ReviewController(reader, null!, new FakeTraceService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } }
        };
    }

    private static ApprovalItemView View(AssigneeRole role) => new(
        Guid.NewGuid(), "Decision required", "{}", "CLAIM-TEST", ApprovalStatus.Pending,
        Priority.Normal, null, role, false, DateTime.UtcNow, null, null);
}
