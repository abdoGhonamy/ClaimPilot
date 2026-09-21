using System.Text.RegularExpressions;

namespace ClaimPilot.Infrastructure.Services.Documents;

public static class PolicyTextNormalizer
{
    // Longest first so "Policy Conditions and Claim Procedures" wins over "Conditions".
    private static readonly string[] Headings =
    {
        "Policy Conditions and Claim Procedures", "Claims & Incident Reporting", "Insuring Agreement",
        "Limits of Liability", "Covered Perils", "Declarations", "Exclusions", "Conditions",
        "Definitions", "Deductible", "Coinsurance", "Execution"
    };

    private static readonly Regex GluedHeading = new(
        @"(?<![a-z])(?<h>" + string.Join("|", Headings.Select(Regex.Escape)) + @")(?=[A-Z][a-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GluedLabel = new(
        @"(?<=[a-z\)\.])(?=(?:Policy Number|Named Insured|Insurer|Policy Period|Covered Location|Total Annual Premium):)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
        text = GluedLabel.Replace(text, "\n");
        text = GluedHeading.Replace(text, "\n${h}\n");
        text = Regex.Replace(text, @"[ ]{2,}", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
