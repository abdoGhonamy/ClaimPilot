using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Controllers;
using ClaimPilot.API.Dtos;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.Tests.Unit;

public sealed class SeedStructuredDataEndpointTests
{
    private static (Policy Policy, PolicyVersion Version) BuildPolicyWithVersion()
    {
        var policy = new Policy
        {
            Id = Guid.NewGuid(),
            PolicyNumber = "AUT-2022",
            ProductLine = "AUT",
            Name = "Auto Comprehensive 2022",
            Status = PolicyStatus.Active
        };
        var version = new PolicyVersion
        {
            Id = Guid.NewGuid(),
            PolicyId = policy.Id,
            Policy = policy,
            Version = 1,
            EffectiveDate = new DateTime(2026, 1, 1)
        };
        policy.Versions.Add(version);
        return (policy, version);
    }

    private static PoliciesController BuildController(
        FakePolicyRepository policies, FakeAuditService audit)
    {
        var controller = new PoliciesController(policies, audit)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private static SeedStructuredDataRequest ValidRequest() => new(
        new[]
        {
            new CoverageSeedItemDto("DEDUCTIBLE", "Per-Claim Deductible", "Deductible", 500, null, "Flat deductible"),
            new CoverageSeedItemDto("LIMIT", "Coverage Limit", "Limit", 5000, null, "Maximum payable")
        },
        new[]
        {
            new ExclusionSeedItemDto("EXC-RACE", "Racing", "Coverage does not apply to racing, speed, or demolition contest.")
        });

    [Fact]
    public async Task SeedStructuredData_WithValidBody_ReturnsInsertedCounts()
    {
        var (policy, _) = BuildPolicyWithVersion();
        var controller = BuildController(new FakePolicyRepository(policy), new FakeAuditService());

        var result = await controller.SeedStructuredData("AUT-2022", 1, ValidRequest(), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<SeedStructuredDataResponse>().Subject;
        dto.CoverageItemsInserted.Should().Be(2);
        dto.CoverageItemsSkipped.Should().Be(0);
        dto.ExclusionsInserted.Should().Be(1);
        dto.ExclusionsSkipped.Should().Be(0);
    }

    [Fact]
    public async Task SeedStructuredData_WithUnknownPolicy_ReturnsNotFound()
    {
        var controller = BuildController(new FakePolicyRepository(), new FakeAuditService());

        var result = await controller.SeedStructuredData("UNKNOWN", 1, ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SeedStructuredData_WithUnknownVersion_ReturnsNotFound()
    {
        var (policy, _) = BuildPolicyWithVersion();
        var controller = BuildController(new FakePolicyRepository(policy), new FakeAuditService());

        var result = await controller.SeedStructuredData("AUT-2022", 99, ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SeedStructuredData_WithInvalidCoverageType_ThrowsValidation()
    {
        var (policy, _) = BuildPolicyWithVersion();
        var controller = BuildController(new FakePolicyRepository(policy), new FakeAuditService());
        var request = ValidRequest() with
        {
            CoverageItems = new[]
            {
                new CoverageSeedItemDto("BOGUS", "Bogus", "NotAType", 100, null, null)
            }
        };

        var act = async () => await controller.SeedStructuredData("AUT-2022", 1, request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*NotAType*");
    }

    [Fact]
    public async Task SeedStructuredData_WritesAuditEntry()
    {
        var (policy, version) = BuildPolicyWithVersion();
        var audit = new FakeAuditService();
        var controller = BuildController(new FakePolicyRepository(policy), audit);

        await controller.SeedStructuredData("AUT-2022", 1, ValidRequest(), CancellationToken.None);

        var entry = audit.Entries
            .Should().ContainSingle(e => e.EntityType == "PolicyVersion" && e.Action == "StructuredDataSeeded").Subject;
        entry.EntityId.Should().Be(version.Id);
        entry.After.Should().Contain("CoverageItemsInserted");
        entry.After.Should().Contain("AUT-2022");
    }
}