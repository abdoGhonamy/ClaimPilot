using FluentAssertions;
using ClaimPilot.Application.Common;

namespace ClaimPilot.Tests;

public class JsonExtractionTests
{
    private sealed record Sample(int Id, string Name);

    [Fact]
    public void PlainArray_IsParsed()
    {
        var result = JsonExtraction.DeserializeArray<Sample>("[{\"Id\":1,\"Name\":\"a\"}]");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("a");
    }

    [Fact]
    public void MarkdownFencedJson_IsParsed()
    {
        var text = "```json\n[{\"Id\":1,\"Name\":\"a\"}]\n```";
        var result = JsonExtraction.DeserializeArray<Sample>(text);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void FenceWithoutLanguageTag_IsParsed()
    {
        var text = "```\n[{\"Id\":2,\"Name\":\"b\"}]\n```";
        var result = JsonExtraction.DeserializeArray<Sample>(text);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(2);
    }

    [Fact]
    public void ArrayEmbeddedInProse_FallsBackToBracketSpan()
    {
        var text = "Here is the answer:\n The list is [{\"Id\":3,\"Name\":\"c\"}] and that is all. Cheers!";
        var result = JsonExtraction.DeserializeArray<Sample>(text);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("c");
    }

    [Fact]
    public void AdversarialConfabulation_WithoutJson_ReturnsEmpty()
    {
        var result = JsonExtraction.DeserializeArray<Sample>("I cannot do that today. Please try again later.");

        result.Should().BeEmpty();
    }

    [Fact]
    public void EmptyOrNull_ReturnsEmpty()
    {
        JsonExtraction.DeserializeArray<Sample>(null).Should().BeEmpty();
        JsonExtraction.DeserializeArray<Sample>("   ").Should().BeEmpty();
    }
}