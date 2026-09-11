using FluentAssertions;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Services;

namespace ClaimPilot.Tests;

public class ApplicableVersionRuleTests
{
    private readonly TrapPolicyVersions _trap = TestCorpus.BuildAutoVersionTrap();
    private readonly Guid _policyId = TestCorpus.PolicyId;

    [Fact]
    public void IncidentInsideV1Window_PicksV1_NotNewest()
    {
        var versions = new[] { _trap.V1, _trap.V2 };
        var selected = ApplicableVersionRule.Select(versions, new DateTime(2023, 3, 10));

        selected.Should().NotBeNull();
        selected!.Version.Should().Be(1);
    }

    [Fact]
    public void IncidentAfterV2Start_PicksV2()
    {
        var versions = new[] { _trap.V1, _trap.V2 };
        var selected = ApplicableVersionRule.Select(versions, new DateTime(2026, 1, 20));

        selected.Should().NotBeNull();
        selected!.Version.Should().Be(2);
    }

    [Fact]
    public void IncidentExactlyOnEffectiveDate_IsInclusive()
    {
        var selected = ApplicableVersionRule.Select(new[] { _trap.V2 }, new DateTime(2025, 6, 1));

        selected.Should().NotBeNull();
        selected!.Version.Should().Be(2);
    }

    [Fact]
    public void IncidentBeforeAnyEffectiveDate_ReturnsNull()
    {
        var selected = ApplicableVersionRule.Select(new[] { _trap.V1, _trap.V2 }, new DateTime(2021, 6, 1));

        selected.Should().BeNull();
    }

    [Fact]
    public void RetiredVersion_IsNeverSelected_EvenIfNewest()
    {
        var retired = new ClaimPilot.Domain.Entities.PolicyVersion
        {
            Id = Guid.NewGuid(),
            PolicyId = _policyId,
            Policy = _trap.V2.Policy,
            Version = 99,
            EffectiveDate = new DateTime(2027, 1, 1),
            Status = PolicyVersionStatus.Retired
        };

        var selected = ApplicableVersionRule.Select(new[] { _trap.V1, _trap.V2, retired }, new DateTime(2028, 1, 1));

        selected.Should().NotBeNull();
        selected!.Version.Should().Be(2);
    }

    [Fact]
    public void SupersededVersion_RemainsEligible_WithinItsWindow()
    {
        // V1 is superseded but still governed the 2023 window.
        var selected = ApplicableVersionRule.Select(new[] { _trap.V1, _trap.V2 }, new DateTime(2023, 3, 10));

        selected!.Version.Should().Be(1);
    }
}