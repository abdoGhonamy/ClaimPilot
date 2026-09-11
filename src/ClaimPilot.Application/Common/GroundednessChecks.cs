using ClaimPilot.Application.Interfaces.Retrieval;

namespace ClaimPilot.Application.Common;

/// <summary>
/// Deterministic groundedness checks. Numbers in an answer must be traceable
/// to the policy corpus; any unsupported amount forces a safe refusal instead
/// of an LLM-invented payout.
/// </summary>
public static class GroundednessChecks
{
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
}