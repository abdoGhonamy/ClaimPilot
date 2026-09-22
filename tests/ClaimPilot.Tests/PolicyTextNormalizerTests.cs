using FluentAssertions;

using ClaimPilot.Infrastructure.Services.Documents;

namespace ClaimPilot.Tests;

public sealed class PolicyTextNormalizerTests
{
    [Fact]
    public void Normalize_glued_heading_splits_own_line()
    {
        const string input = "ExclusionsThis policy does not cover flood or water damage.";
        var result = PolicyTextNormalizer.Normalize(input);
        result.Should().StartWith("Exclusions\n");
    }

    [Fact]
    public void Normalize_glued_label_starts_line()
    {
        const string input = "PolicyPolicy Number: HO-123456";
        var result = PolicyTextNormalizer.Normalize(input);
        result.Should().Contain("\nPolicy Number: HO-123456");
    }

    [Fact]
    public void Normalize_collapses_multiple_spaces()
    {
        var result = PolicyTextNormalizer.Normalize("Covered   perils   include   fire.");
        result.Should().Be("Covered perils include fire.");
    }

    [Fact]
    public void Normalize_returns_empty_for_blank()
    {
        PolicyTextNormalizer.Normalize(string.Empty).Should().BeEmpty();
    }
}
