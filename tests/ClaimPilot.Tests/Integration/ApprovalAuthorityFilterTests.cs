using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Security.Claims;

using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Services;
using ClaimPilot.Domain.Enums;
using ClaimPilot.API.Auth;
using AuthApprovalAction = ClaimPilot.API.Auth.ApprovalAction;

namespace ClaimPilot.Tests.Integration;

public class ApprovalAuthorityFilterTests
{
    private static readonly AuthorityService Authority = new(Options.Create(new ApprovalAuthorityOptions
    {
        Adjuster = 10_000m,
        Supervisor = 100_000m,
        Director = 1_000_000_000m
    }));

    private static ClaimsPrincipal Principal(params string[] roles)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var role in roles) identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(identity);
    }

    private static ApprovalItemDetail Item(Guid id, decimal amount, AssigneeRole? assignedTo = AssigneeRole.Adjuster) => new(
        id,
        "Decision required — CLAIM-2024-070",
        "{}",
        Guid.NewGuid(),
        "CLAIM-2024-070",
        ApprovalStatus.Pending,
        Priority.High,
        null,
        assignedTo,
        "{}",
        "{}",
        null,
        amount,
        DateTime.UtcNow.AddHours(-2),
        null,
        Array.Empty<ApprovalHistoryView>());

    private static async Task<AuthorizationFilterContext> RunAsync(
        ClaimsPrincipal? user, string? routeId, ApprovalItemDetail? item, AuthApprovalAction action)
    {
        var http = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) };
        if (routeId is not null) http.Request.RouteValues["approvalItemId"] = routeId;

        var reader = new FakeApprovalQueueReader();
        if (item is not null) reader.AddItem(item);

        http.RequestServices = new ServiceCollection()
            .AddSingleton<IApprovalQueueReader>(reader)
            .AddSingleton<IAuthorityService>(Authority)
            .AddSingleton<ILogger<RequireApprovalAuthorityAttribute>>(
                NullLogger<RequireApprovalAuthorityAttribute>.Instance)
            .BuildServiceProvider();

        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var filter = new RequireApprovalAuthorityAttribute(action);
        await filter.OnAuthorizationAsync(context);
        return context;
    }

    private static ProblemDetails DeniedDetails(AuthorizationFilterContext context)
        => ((ObjectResult)context.Result!).Value as ProblemDetails
           ?? new ProblemDetails { Status = -1, Detail = string.Empty };

    [Fact]
    public async Task Unauthenticated_Unauthorized()
    {
        var context = await RunAsync(user: null, routeId: null, item: null, AuthApprovalAction.Approve);

        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task PrincipalWithoutRoles_Forbidden()
    {
        var context = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity("test")), routeId: null, item: null, AuthApprovalAction.Approve);

        context.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task MissingOrInvalidRouteValue_BadRequest()
    {
        var context = await RunAsync(Principal("Adjuster"), routeId: "not-a-guid", item: null, AuthApprovalAction.Approve);

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UnknownItem_NotFound()
    {
        var context = await RunAsync(Principal("Adjuster"), routeId: Guid.NewGuid().ToString(), item: null, AuthApprovalAction.Approve);

        context.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData(5_000)]
    public async Task Adjuster_WithinThreshold_Allowed(int amount)
    {
        var item = Item(Guid.NewGuid(), amount, AssigneeRole.Adjuster);
        var context = await RunAsync(Principal("Adjuster"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Adjuster_OverThreshold_ForbiddenWithEscalationHint()
    {
        var item = Item(Guid.NewGuid(), 50_000m, AssigneeRole.Adjuster);
        var context = await RunAsync(Principal("Adjuster"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        DeniedDetails(context).Detail.Should().Contain("escalate");
    }

    [Fact]
    public async Task Supervisor_OnOwnItem_OverAdjusterThreshold_Allowed()
    {
        var item = Item(Guid.NewGuid(), 50_000m, AssigneeRole.Supervisor);
        var context = await RunAsync(Principal("Adjuster", "Supervisor"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Supervisor_OverSupervisorThreshold_Forbidden()
    {
        var item = Item(Guid.NewGuid(), 500_000m, AssigneeRole.Supervisor);
        var context = await RunAsync(Principal("Adjuster", "Supervisor"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Director_CanApproveAnyValidAmount()
    {
        var item = Item(Guid.NewGuid(), 999_999_999m, AssigneeRole.Director);
        var context = await RunAsync(Principal("Adjuster", "Supervisor", "Director"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Reject_SkipsAmountCheck()
    {
        var item = Item(Guid.NewGuid(), 50_000m, AssigneeRole.Adjuster);
        var context = await RunAsync(Principal("Adjuster"), item.Id.ToString(), item, AuthApprovalAction.Reject);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task HigherRole_CannotBypassAssigneeRule()
    {
        var item = Item(Guid.NewGuid(), 5_000m, AssigneeRole.Adjuster);
        var context = await RunAsync(Principal("Supervisor", "Director"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        DeniedDetails(context).Detail.Should().Contain("This item is assigned to a Adjuster");
    }

    [Fact]
    public async Task WrongAssigneeRole_ForbiddenWithDetail()
    {
        var item = Item(Guid.NewGuid(), 5_000m, AssigneeRole.Supervisor);
        var context = await RunAsync(Principal("Adjuster"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        DeniedDetails(context).Detail.Should().Be("This item is assigned to a Supervisor. Only users with that role can act on it.");
    }

    [Fact]
    public async Task UnassignedItem_Conflict()
    {
        var item = Item(Guid.NewGuid(), 5_000m, null);
        var context = await RunAsync(Principal("Adjuster"), item.Id.ToString(), item, AuthApprovalAction.Approve);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Reject_StillGatedByAssignee()
    {
        var item = Item(Guid.NewGuid(), 5_000m, AssigneeRole.Adjuster);
        var context = await RunAsync(Principal("Supervisor"), item.Id.ToString(), item, AuthApprovalAction.Reject);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Assign_RequiresSupervisorOrDirector()
    {
        var item = Item(Guid.NewGuid(), 5_000m, null);
        var context = await RunAsync(Principal("Adjuster"), item.Id.ToString(), item, AuthApprovalAction.Assign);

        context.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Assign_Supervisor_Allowed()
    {
        var item = Item(Guid.NewGuid(), 5_000m, null);
        var context = await RunAsync(Principal("Supervisor"), item.Id.ToString(), item, AuthApprovalAction.Assign);

        context.Result.Should().BeNull();
    }
}