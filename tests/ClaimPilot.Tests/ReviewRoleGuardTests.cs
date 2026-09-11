using System.Reflection;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;

namespace ClaimPilot.Tests;

/// <summary>
/// Weak (structural) regression guard for the API role model. Decision-changing
/// review actions must require Supervisor; the queue itself is open to
/// Adjusters and Supervisors, never to Viewers.
/// </summary>
public class ReviewRoleGuardTests
{
    private const string SupervisorOnly = "Supervisor";
    private const string AdjusterSupervisor = "Adjuster,Supervisor";

    private static Type? ReviewControllerType { get; } =
        Type.GetType("ClaimPilot.API.Controllers.ReviewController, ClaimPilot.API");

    [Fact]
    public void ReviewController_RequiresAdjusterOrSupervisor()
    {
        ReviewControllerType.Should().NotBeNull();

        var roles = ReviewControllerType!.GetCustomAttribute<AuthorizeAttribute>()?.Roles;

        roles.Should().Be(AdjusterSupervisor, "the review queue is visible to adjusters and supervisors");
    }

    [Theory]
    [InlineData("Approve")]
    [InlineData("Reject")]
    [InlineData("Edit")]
    [InlineData("ReReview")]
    public void DecisionActions_AreSupervisorOnly(string methodName)
    {
        var method = ReviewControllerType!.GetMethod(methodName);
        method.Should().NotBeNull($"action {methodName} must exist");

        var roles = method!.GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Roles)
            .Where(r => r is not null)
            .Select(r => r!)
            .DefaultIfEmpty("(none)");

        roles.Should().Contain(SupervisorOnly, $"{methodName} mutates a final decision and must be supervisor-only");
    }
}