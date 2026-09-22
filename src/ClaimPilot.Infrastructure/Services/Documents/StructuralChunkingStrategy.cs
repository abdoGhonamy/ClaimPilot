using System.Text;
using System.Text.RegularExpressions;

using ClaimPilot.Application.Interfaces.Documents;

namespace ClaimPilot.Infrastructure.Services.Documents;

/// <summary>
/// Heading-aware chunking strategy. Splits the document text at detected
/// headings (known insurance headings, markdown "#" lines, numbered headings)
/// and emits one or more chunks per section. Sets Section, Clause and Page on
/// every chunk so citations and downstream extraction can reference exact
/// locations.
/// </summary>
public sealed class StructuralChunkingStrategy : IChunkingStrategy
{
    private const int MaxChunkChars = 1000;
    private const int MaxSectionChars = 200;

    private static readonly HashSet<string> KnownHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Declarations",
        "Insuring Agreement",
        "Covered Perils",
        "Exclusions",
        "Conditions",
        "Policy Conditions and Claim Procedures",
        "Claims & Incident Reporting",
        "Limits of Liability",
        "Deductible",
        "Coinsurance",
        "Definitions",
        "Execution"
    };

    private static readonly Regex MarkdownHeading = new(
        @"^#{1,6}\s+(?<t>.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedHeading = new(
        @"^(?:SECTION\s+\d+[\.:]?|\d+\.)\s+(?<t>\S.{0,80})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<IngestedChunkPayload> Chunk(ExtractedDocument doc)
    {
        var chunks = new List<IngestedChunkPayload>();
        var buffer = new StringBuilder();
        var currentSection = "General";
        int? currentPage = null;

        void Flush()
        {
            var text = buffer.ToString().Trim();
            buffer.Clear();
            if (text.Length == 0) return;

            var parts = SplitAtBoundaries(text, MaxChunkChars);
            for (var i = 0; i < parts.Count; i++)
            {
                var clause = parts.Count == 1
                    ? currentSection
                    : $"{currentSection} ({i + 1})";

                chunks.Add(new IngestedChunkPayload
                {
                    Content = parts[i],
                    Section = Truncate(currentSection, MaxSectionChars),
                    Clause = Truncate(clause, MaxSectionChars),
                    Page = currentPage
                });
            }
        }

        foreach (var section in doc.Sections)
        {
            var normalized = PolicyTextNormalizer.Normalize(section.Text ?? string.Empty);
            foreach (var rawLine in normalized.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                var heading = DetectHeading(line);
                if (heading is not null)
                {
                    Flush();
                    currentSection = heading;
                    currentPage = section.Page;
                    continue;
                }

                currentPage ??= section.Page;
                buffer.AppendLine(line);
            }
        }

        Flush();
        return chunks;
    }

    private static string? DetectHeading(string line)
    {
        var trimmed = line.TrimEnd(':').Trim();

        if (KnownHeadings.Contains(trimmed))
            return trimmed;

        var md = MarkdownHeading.Match(line);
        if (md.Success) return md.Groups["t"].Value.Trim().TrimEnd(':').Trim();

        var num = NumberedHeading.Match(line);
        if (num.Success) return num.Groups["t"].Value.Trim().TrimEnd(':').Trim();

        return null;
    }

    private static List<string> SplitAtBoundaries(string text, int max)
    {
        if (text.Length <= max) return new List<string> { text };

        var result = new List<string>();
        var current = new StringBuilder();

        // Prefer paragraph boundaries, then sentence boundaries.
        foreach (var paragraph in Regex.Split(text, @"\n{2,}"))
        {
            var block = paragraph.Trim();
            if (block.Length == 0) continue;

            if (block.Length <= max)
            {
                if (current.Length + block.Length + 2 > max && current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                current.Append(block).Append("\n\n");
                continue;
            }

            // Paragraph too large: split at sentence boundaries.
            foreach (var sentence in Regex.Split(block, @"(?<=[\.\!\?])\s+"))
            {
                if (current.Length + sentence.Length > max && current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                current.Append(sentence).Append(' ');
            }
        }

        if (current.Length > 0) result.Add(current.ToString().Trim());
        return result;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max);
}