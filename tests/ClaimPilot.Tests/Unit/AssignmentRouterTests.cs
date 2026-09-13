using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Services;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Tests.Unit;

public class AssignmentRouterTests
{
    private static readonly IAuthorityService Authority = new AuthorityService(Options.Create(new ApprovalAuthorityOptions
    {
        Adjuster = 10_000m,
        Supervisor = 100_000m,
        Director = 1_000_000_000m
    }));

    [Theory]
    [InlineData(Priority.Normal, 4_000, AssigneeRole.Adjuster)]
    [InlineData(Priority.Normal, 45_000, AssigneeRole.Supervisor)]
    [InlineData(Priority.Normal, 250_000, AssigneeRole.Director)]
    [InlineData(Priority.High, 2_000, AssigneeRole.Supervisor)]
    [InlineData(Priority.High, 80_000, AssigneeRole.Supervisor)]
    [InlineData(Priority.High, 150_000, AssigneeRole.Director)]
    [InlineData(Priority.Critical, 999, AssigneeRole.Director)]
    [InlineData(Priority.Critical, 50_000, AssigneeRole.Director)]
    [InlineData(Priority.Critical, 900_000, AssigneeRole.Director)]
    [InlineData(Priority.Low, 200, AssigneeRole.Adjuster)]
    [InlineData(Priority.Low, 200_000, AssigneeRole.Director)]
    public void Compute_RoutesByTierAndAmount(Priority priority, int amount, AssigneeRole expected)
    {
        var actual = AssignmentRouter.Compute(priority, amount, Authority);

        Assert.Equal(expected, actual);
    }
}