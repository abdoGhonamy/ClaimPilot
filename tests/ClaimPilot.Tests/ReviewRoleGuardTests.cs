using System.Reflection;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;

using ClaimPilot.API.Auth;

namespace ClaimPilot.Tests;

/// <summary>
/// Weak (structural) regression guard for the API role model. Decision-changing
/// review actions (approve/reject/edit) are gated by RequireApprovalAuthority,
/// which enforces per-role monetary thresholds; assign and priority override stay
/// supervisor+director; the working queue is open to adjusters, supervisors and
/// directors, never to viewers.
/// </summary>
public class ReviewRoleGuardTests
{
    private const string AdjusterSupervisorDirector = "Adjuster,Supervisor,Director";
    private const string SupervisorDirector = "Supervisor,Director";

    private static Type? ReviewControllerType { get; } =
        Type.GetType("ClaimPilot.API.Controllers.ReviewController, ClaimPilot.API");

    [Fact]
    public void ReviewController_RequiresAdjusterSupervisorOrDirector()
    {
        ReviewControllerType.Should().NotBeNull();

        var roles = ReviewControllerType!.GetCustomAttribute<AuthorizeAttribute>()?.Roles;

        roles.Should().Be(AdjusterSupervisorDirector,
            "the review queue is visible to adjusters, supervisors and directors");
    }

    [Theory]
    [InlineData("Approve", ApprovalAction.Approve)]
    [InlineData("Reject", ApprovalAction.Reject)]
    [InlineData("Edit", ApprovalAction.Edit)]
    public void DecisionActions_AreGatedByApprovalAuthority(string methodName, ApprovalAction expected)
    {
        var method = ReviewControllerType!.GetMethod(methodName);
        method.Should().NotBeNull($"action {methodName} must exist");

        var guard = method!.GetCustomAttributes<RequireApprovalAuthorityAttribute>().SingleOrDefault();
        guard.Should().NotBeNull($"{methodName} must be gated by RequireApprovalAuthority");
        guard!.For.Should().Be(expected);

        method.GetCustomAttributes<AuthorizeAttribute>()
            .Where(a => a.Roles is not null)
            .Should().BeEmpty($"{methodName} must not be restricted by a plain role attribute");
    }

    [Theory]
    [InlineData("ReReview")]
    [InlineData("Escalate")]
    public void OpenActions_AllowAdjustersSupervisorsDirectors(string methodName)
    {
        var method = ReviewControllerType!.GetMethod(methodName);
        method.Should().NotBeNull($"action {methodName} must exist");

        method!.GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Roles)
            .Should().Contain(AdjusterSupervisorDirector,
                $"{methodName} is open to adjusters, supervisors and directors");
    }

    [Theory]
    [InlineData("Assign")]
    [InlineData("OverridePriority")]
    public void SupervisorOnlyActions_ExcludeAdjusters(string methodName)
    {
        var method = ReviewControllerType!.GetMethod(methodName);
        method.Should().NotBeNull($"action {methodName} must exist");

        method!.GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Roles)
            .Should().Contain(SupervisorDirector,
                $"{methodName} requires supervisor or director");
    }

    [Fact]
    public void Viewer_NeverHasAnActionForwarded()
    {
        var forwardersToViewer = ReviewControllerType!
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AuthorizeAttribute>()
                .Any(a => a.Roles != null && a.Roles.Contains("Viewer")))
            .Select(m => m.Name);

        forwardersToViewer.Should().BeEmpty("viewers must never be permitted to act");
    }
}