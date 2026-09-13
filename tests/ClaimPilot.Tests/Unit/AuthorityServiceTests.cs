using FluentAssertions;

using Microsoft.Extensions.Options;

using System.Security.Claims;

using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Services;

namespace ClaimPilot.Tests.Unit;

public class AuthorityServiceTests
{
    private readonly AuthorityService _service = new(Options.Create(new ApprovalAuthorityOptions
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

    [Theory]
    [InlineData(1_000, 10_000, true)]
    [InlineData(10_000, 10_000, true)]
    [InlineData(10_001, 10_000, false)]
    [InlineData(50_000, 100_000, true)]
    [InlineData(100_000, 100_000, true)]
    [InlineData(100_001, 100_000, false)]
    [InlineData(999_999_999, 1_000_000_000, true)]
    [InlineData(1_000_000_001, 1_000_000_000, false)]
    public async Task CanApprove_ApproveHonorsRoleThreshold(decimal amount, decimal threshold, bool allowed)
    {
        var user = Principal("Adjuster");
        if (threshold == 100_000m) user = Principal("Adjuster", "Supervisor");
        if (threshold == 1_000_000_000m) user = Principal("Adjuster", "Supervisor", "Director");

        var result = await _service.CanApproveAsync(user, amount, "Approve", CancellationToken.None);

        result.Should().Be(allowed);
    }

    [Fact]
    public async Task CanApprove_EditHonorsThreshold()
    {
        var adjuster = Principal("Adjuster");

        (await _service.CanApproveAsync(adjuster, 9_999m, "Edit", CancellationToken.None)).Should().BeTrue();
        (await _service.CanApproveAsync(adjuster, 10_001m, "Edit", CancellationToken.None)).Should().BeFalse();
    }

    [Theory]
    [InlineData(10_000_000)]
    [InlineData(999_999_999)]
    public async Task CanApprove_NeverRestsAmountGate(decimal amount)
    {
        var adjuster = Principal("Adjuster");

        (await _service.CanApproveAsync(adjuster, amount, "Reject", CancellationToken.None)).Should().BeTrue();
        (await _service.CanApproveAsync(adjuster, amount, "Assign", CancellationToken.None)).Should().BeTrue();
        (await _service.CanApproveAsync(adjuster, amount, "Escalate", CancellationToken.None)).Should().BeTrue();
        (await _service.CanApproveAsync(adjuster, amount, "View", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CanApprove_UnknownUserHasNoAuthority()
    {
        (await _service.CanApproveAsync(Principal(), 1m, "Approve", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public void GetThresholdForUser_PicksHighestGrantedRole()
    {
        _service.GetThresholdForUser(Principal("Adjuster")).Should().Be(10_000m);
        _service.GetThresholdForUser(Principal("Adjuster", "Supervisor")).Should().Be(100_000m);
        _service.GetThresholdForUser(Principal("Adjuster", "Supervisor", "Director")).Should().Be(1_000_000_000m);
        _service.GetThresholdForUser(Principal()).Should().Be(0m);
    }
}