using System.Text.Json;

using FluentAssertions;

using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Services;
using ClaimPilot.Application.Services.Agents;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Tests;

public sealed class ExclusionAnalystAgentTests
{
    [Fact]
    public async Task Execute_WhenClaimMentionsSpeedingContest_AppliesRacingExclusion()
    {
        var tools = new FakeToolRegistry
        {
            Exclusions = new[]
            {
                new PolicyExclusionLine("EXC-002", "Racing",
                    "Coverage does not apply to any loss occurring while the insured vehicle is used in any racing, speed, or demolition contest.")
            },
            ExclusionCheckResults = new Dictionary<string, ExclusionCheckResult>
            {
                ["EXC-002"] = new(true, "EXC-002", "Racing", "Matched keywords: speed, contest")
            }
        };
        var agent = new ExclusionAnalystAgent(tools, new FakeLLMProvider(), new OrchestrationEventSink());
        var context = new AgentContext
        {
            RunId = Guid.NewGuid(),
            ClaimId = Guid.NewGuid(),
            ClaimNumber = "CLAIM-2026-EXCLUSION",
            PolicyNumber = "AUT-2022",
            IncidentDate = new DateTime(2023, 6, 15),
            ClaimAmount = 5_000m,
            ClaimDescription = "My car has got damaged cause of a speeding contest",
            AllowedTools = AgentToolMap.AllowedFor(AgentType.ExclusionAnalyst),
            State = new Dictionary<string, string>()
        };

        var result = await agent.ExecuteAsync(context, CancellationToken.None);

        using var output = JsonDocument.Parse(result.Output);
        var codes = output.RootElement.GetProperty("applicable_codes");
        codes.GetArrayLength().Should().Be(1);
        codes[0].GetString().Should().Be("EXC-002");
        tools.Calls.Count(call => call.Tool == ToolName.CheckExclusion).Should().Be(1);
    }
}
