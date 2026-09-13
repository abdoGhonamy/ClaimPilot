using System.Text.RegularExpressions;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Controllers;
using ClaimPilot.API.Dtos;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.Tests.Unit;

public sealed class CreateClaimEndpointTests
{
    private static readonly Policy TestPolicy = new()
    {
        Id = Guid.NewGuid(),
        PolicyNumber = "AUT-2022",
        ProductLine = "AUT",
        Name = "Auto Comprehensive 2022",
        Status = PolicyStatus.Active
    };

    private static ClaimsController BuildController(
        FakeClaimRepository claims, FakePolicyRepository policies, FakeAuditService audit)
    {
        var controller = new ClaimsController(
            claims, policies, audit, new FakeStorageService(), new NullTraceViewBuilder());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ClaimsController BuildController(FakeClaimRepository claims, FakeAuditService audit)
        => BuildController(claims, new FakePolicyRepository(TestPolicy), audit);

    private static CreateClaimRequest ValidRequest() =>
        new("AUT-2022", new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc), 1400.00m,
            "My car was hit by another vehicle at the intersection of Main and 5th.");

    [Fact]
    public async Task CreateClaim_WithValidRequest_Returns201()
    {
        var claims = new FakeClaimRepository { NextClaimNumber = "CLAIM-2026-001" };
        var audit = new FakeAuditService();
        var controller = BuildController(claims, audit);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.ActionName.Should().Be(nameof(ClaimsController.Get));
        var dto = created.Value.Should().BeOfType<ClaimDto>().Subject;
        dto.ClaimNumber.Should().Be("CLAIM-2026-001");
        dto.Status.Should().Be(ClaimStatus.Submitted);
        claims.Claims.Should().ContainSingle(c => c.Id == dto.Id);
    }

    [Fact]
    public async Task CreateClaim_WithUnknownPolicy_Returns400()
    {
        var controller = BuildController(new FakeClaimRepository(), new FakePolicyRepository(), new FakeAuditService());

        var act = async () => await controller.Create(ValidRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Policy 'AUT-2022' not found.");
    }

    [Fact]
    public async Task CreateClaim_WithNegativeAmount_Returns400()
    {
        var controller = BuildController(new FakeClaimRepository(), new FakeAuditService());
        var request = ValidRequest() with { ClaimAmount = -500m };

        await Assert.ThrowsAsync<ValidationException>(() => controller.Create(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateClaim_WithFutureIncidentDate_Returns400()
    {
        var controller = BuildController(new FakeClaimRepository(), new FakeAuditService());
        var request = ValidRequest() with { IncidentDate = DateTime.UtcNow.AddDays(30) };

        await Assert.ThrowsAsync<ValidationException>(() => controller.Create(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateClaim_GeneratesClaimNumber_InCorrectFormat()
    {
        var claims = new FakeClaimRepository { NextClaimNumber = "CLAIM-2026-042" };
        var controller = BuildController(claims, new FakeAuditService());

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        var dto = ((CreatedAtActionResult)result.Result!).Value.Should().BeOfType<ClaimDto>().Subject;
        Regex.IsMatch(dto.ClaimNumber, @"^CLAIM-\d{4}-\d{3}$").Should().BeTrue();
        claims.Claims.Single().ClaimNumber.Should().Be(dto.ClaimNumber);
    }

    [Fact]
    public async Task CreateClaim_WritesAuditLog()
    {
        var claims = new FakeClaimRepository { NextClaimNumber = "CLAIM-2026-001" };
        var audit = new FakeAuditService();
        var controller = BuildController(claims, audit);

        await controller.Create(ValidRequest(), CancellationToken.None);

        var entry = audit.Entries.Should().ContainSingle(e => e.EntityType == "Claim" && e.Action == "Created").Subject;
        entry.After.Should().Contain("CLAIM-2026-001");
        entry.EntityId.Should().Be(claims.Claims.Single().Id);
    }
}
