using System.Text.RegularExpressions;

using ClaimPilot.Application.Interfaces.Retrieval;

namespace ClaimPilot.Application.Common;

/// <summary>
/// Deterministic groundedness checks. Numbers in an answer must be traceable
/// to the policy corpus; any unsupported amount forces a safe refusal instead
/// of an LLM-invented payout.
/// </summary>
public static class GroundednessChecks
{
    private static readonly Regex SourceCitation = new(
        @"\[source:\s*(?<section>[^/\]]+)\s*/\s*(?<clause>[^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UnsafeDirective = new(
        @"\b(ignore\s+(all\s+)?(previous|prior)\s+instructions?|reveal\s+(the\s+)?system\s+prompt|call\s+(a\s+)?tool|approve\s+(this|the)\s+claim)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool ContainsUnsupportedNumbers(string answer, IReadOnlyList<RetrievedChunk> chunks)
    {
        var corpus = string.Join(' ', chunks.Select(c => c.Text));
        var tokens = answer.Split([' ', '\n', ',', '.', '$'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (decimal.TryParse(token, out var n) && n > 0 && !corpus.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                var bare = n.ToString();
                if (!corpus.Contains(bare)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Requires every source citation in a model answer to identify a chunk that was
    /// actually retrieved for the pinned policy version. A prompt cannot grant the
    /// model permission to cite an arbitrary section or omit its evidence.
    /// </summary>
    public static bool HasOnlyRetrievedSourceCitations(string answer, IReadOnlyList<RetrievedChunk> chunks)
    {
        var matches = SourceCitation.Matches(answer);
        if (matches.Count == 0)
            return false;

        return matches.All(match => chunks.Any(chunk =>
            string.Equals(chunk.Section, match.Groups["section"].Value.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(chunk.Clause, match.Groups["clause"].Value.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Model output is display data, never an instruction. Reject common attempts to
    /// turn an answer into a control-plane instruction even when an adversarial chunk
    /// contains the same text or a valid-looking citation.
    /// </summary>
    public static bool ContainsUnsafeDirective(string text) => UnsafeDirective.IsMatch(text);
}
