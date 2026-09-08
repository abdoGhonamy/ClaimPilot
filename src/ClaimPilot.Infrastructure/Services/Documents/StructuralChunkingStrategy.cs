using System.Text.RegularExpressions;

using ClaimPilot.Application.Interfaces.Documents;

namespace ClaimPilot.Infrastructure.Services.Documents;

/// <summary>
/// Structural chunking strategy. Rather than fixed page-sized slices, chunks are
/// derived from detected sections, clauses, benefits, exclusions and limits.
/// </summary>
public sealed class StructuralChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex ClausePattern = new(
        @"^\s*\d+(\.\d+)*[a-z]?[\.\)]\s+\S+",
        RegexOptions.Compiled);

    private const int MaxChunkLength = 1200;

    public IReadOnlyList<IngestedChunkPayload> Chunk(ExtractedDocument document)
    {
        var chunks = new List<IngestedChunkPayload>();
        var currentSection = "general";
        var currentClause = string.Empty;
        var buffer = new List<string>();

        foreach (var section in document.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Title) && section.Title != "page")
            {
                Flush(buffer, chunks, currentSection, currentClause);
                currentSection = Normalize(section.Title);
                currentClause = string.Empty;
            }

            var text = section.Text;
            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                var match = ClausePattern.Match(trimmed);
                if (match.Success)
                {
                    Flush(buffer, chunks, currentSection, currentClause);
                    currentClause = Normalize(trimmed);
                }

                if (trimmed.StartsWith("## ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("### ", StringComparison.OrdinalIgnoreCase))
                {
                    Flush(buffer, chunks, currentSection, currentClause);
                    currentSection = Normalize(trimmed.TrimStart('#').Trim());
                }

                buffer.Add(trimmed);
                if (buffer.Sum(b => b.Length) >= MaxChunkLength)
                    Flush(buffer, chunks, currentSection, currentClause);
            }
        }

        Flush(buffer, chunks, currentSection, currentClause);
        return chunks;
    }

    private static void Flush(List<string> buffer, List<IngestedChunkPayload> chunks, string section, string clause)
    {
        if (buffer.Count == 0) return;
        var content = string.Join(' ', buffer).Trim();
        if (content.Length > 0)
        {
            chunks.Add(new IngestedChunkPayload
            {
                Content = content,
                Section = section,
                Clause = clause
            });
        }
        buffer.Clear();
    }

    private static string Normalize(string value)
    {
        var cleaned = Regex.Replace(value, @"\s+", " ").Trim();
        return cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }
}