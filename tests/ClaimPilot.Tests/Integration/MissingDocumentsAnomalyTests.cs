using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ClaimPilot.Application.Services;
using ClaimPilot.Application.Services.Agents;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Tests.Integration;

/// <summary>
/// Verifies the missing_documents anomaly fix: the orchestrator now reads the
/// actual ClaimDocuments table (via HasDocumentsAsync) instead of hardcoding false.
/// </summary>
public sealed class MissingDocumentsAnomalyTests
{
    private static Claim BuildClaim() => new()
    {
        Id = Guid.NewGuid(),
        ClaimNumber = "CLAIM-2026-003",
        PolicyNumber = "AUT-2022",
        IncidentDate = new DateTime(2023, 3, 10, 0, 0, 0, DateTimeKind.Utc),
        ClaimAmount = 1400m,
        Description = "Damage from a collision at the intersection of Main and Fifth Avenue.",
        Status = ClaimStatus.Submitted,
        CreatedAt = DateTime.UtcNow
    };

    private static SupervisorOrchestrator BuildOrchestrator(FakeClaimRepository claims)
    {
        var llm = new FakeLLMProvider { NextText = "The claim is covered under the applicable policy terms." };
        var tools = new FakeToolRegistry();
        var sink = new OrchestrationEventSink();

        return new SupervisorOrchestrator(
            Options.Create(new OrchestratorOptions()),
            new FakePolicyRepository(TestCorpus.BuildAutoVersionTrap().Policy),
            claims,
            new DeterministicAdjudicationEngine(),
            new FakeApprovalRepository(),
            new FakeTraceService(),
            new FakeAuditService(),
            new FakeUsageTracker(),
            sink,
            new CoverageMatcherAgent(tools, llm, sink),
            new ExclusionAnalystAgent(tools, llm, sink),
            new AnomalyDetectorAgent(tools, sink),
            new AdjudicationDrafterAgent(llm, tools, sink),
            NullLogger<SupervisorOrchestrator>.Instance);
    }

    [Fact]
    public async Task PostClaim_ThenAdjudicate_HasDocumentsIsTrue()
    {
        var claim = BuildClaim();
        var claims = new FakeClaimRepository();
        claims.Claims.Add(claim);
        claims.Documents.Add(new ClaimDocument
        {
            Id = Guid.NewGuid(),
            ClaimId = claim.Id,
            Claim = claim,
            FileName = "police_report.pdf",
            ContentType = "application/pdf"
        });

        var orchestrator = BuildOrchestrator(claims);

        var result = await orchestrator.RunAsync(claim.Id, null, CancellationToken.None);

        result.Status.Should().Be("completed");
        result.AnomalySummary.Should().NotContain(a => a.Contains("missing_documents"),
            "a claim with an uploaded document must not trigger missing_documents");
    }

    [Fact]
    public async Task PostClaim_ThenAdjudicate_NoDocuments_FiresMissingDocuments()
    {
        var claim = BuildClaim();
        var claims = new FakeClaimRepository();
        claims.Claims.Add(claim);

        var orchestrator = BuildOrchestrator(claims);

        var result = await orchestrator.RunAsync(claim.Id, null, CancellationToken.None);

        result.Status.Should().Be("completed");
        result.AnomalySummary.Should().Contain(a => a.Contains("missing_documents"),
            "a claim with no documents must still be flagged");
    }
}
