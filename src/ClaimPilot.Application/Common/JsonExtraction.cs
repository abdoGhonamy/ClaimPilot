using System.Text.Json;

namespace ClaimPilot.Application.Common;

/// <summary>
/// Tolerant extraction of a JSON array from LLM responses. Models often wrap
/// JSON in markdown fences, prose, or a leading newline; strict parsing then
/// fails. This helper falls back to the first balanced array span before giving
/// up (never throws).
/// </summary>
public static class JsonExtraction
{
    public static IReadOnlyList<T> DeserializeArray<T>(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<T>();

        foreach (var candidate in CandidateSpans(text))
        {
            try
            {
                return JsonSerializer.Deserialize<List<T>>(candidate)
                    ?? new List<T>();
            }
            catch (JsonException)
            {
                // try next candidate
            }
        }

        return Array.Empty<T>();
    }

    private static IEnumerable<string> CandidateSpans(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                var code = trimmed[(firstNewline + 1)..].Trim();
                const string closing = "```";
                var end = code.LastIndexOf(closing, StringComparison.Ordinal);
                trimmed = end > 0 ? code[..end].Trim() : code;
            }
        }

        yield return trimmed;

        var start = trimmed.IndexOf('[');
        var close = trimmed.LastIndexOf(']');
        if (start >= 0 && close > start)
            yield return trimmed[start..(close + 1)];
    }
}