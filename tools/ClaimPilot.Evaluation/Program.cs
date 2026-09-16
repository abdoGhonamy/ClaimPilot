using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using ClaimPilot.Infrastructure.Data.Seed;

var options = EvaluationOptions.Parse(args);
using var client = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };

var login = await client.PostAsJsonAsync("/api/auth/login", new
{
    username = options.Username,
    password = options.Password
});
login.EnsureSuccessStatusCode();
var session = await login.Content.ReadFromJsonAsync<LoginResponse>()
    ?? throw new InvalidOperationException("Login returned no token.");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

var cases = options.Categories.Count == 0
    ? EvaluationCorpus.All
    : EvaluationCorpus.All.Where(c => options.Categories.Contains(c.Category)).ToList();
var results = new List<CaseResult>();

foreach (var test in cases)
{
    try
    {
        var response = await client.PostAsJsonAsync("/api/ask", new
        {
            question = test.Question,
            policyNumber = test.PolicyNumber,
            incidentDate = test.IncidentDate
        });

        if (!response.IsSuccessStatusCode)
        {
            results.Add(CaseResult.HttpFailure(test, (int)response.StatusCode,
                await response.Content.ReadAsStringAsync()));
            continue;
        }

        var answer = await response.Content.ReadFromJsonAsync<AskResponse>();
        if (answer is null)
        {
            results.Add(CaseResult.HttpFailure(test, 200, "Empty JSON response."));
            continue;
        }

        var expectedText = test.ExpectFullMatch ?? test.ExpectedSubstring;
        var expectedTextMatched = test.ExpectRefusal
            ? answer.Refused && answer.Answer.Equals("Not enough information in the policy corpus to determine this.", StringComparison.Ordinal)
            : answer.Answer.Contains(expectedText, StringComparison.OrdinalIgnoreCase);
        var refusalMatched = answer.Refused == test.ExpectRefusal;
        var citationMatched = test.ExpectRefusal || answer.Citations.Count > 0;
        results.Add(new CaseResult(test.Id, test.Category.ToString(), true, answer.Refused,
            test.ExpectRefusal, expectedTextMatched, refusalMatched, citationMatched,
            answer.Citations.Count, answer.Answer, null, null));
    }
    catch (Exception ex)
    {
        results.Add(CaseResult.Exception(test, ex.Message));
    }
}

var report = EvaluationReport.Create(options.BaseUrl, results);
var outputPath = options.ReportPath ?? Path.Combine("artifacts", $"evaluation-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Evaluation: {report.Passed}/{report.Total} passed ({report.AnswerHitRate:P1} answer hit rate)");
Console.WriteLine($"Refusal correctness: {report.RefusalCorrectness:P1}; citation rate: {report.CitationRate:P1}");
Console.WriteLine($"Report: {Path.GetFullPath(outputPath)}");
foreach (var failed in results.Where(x => !x.Passed))
    Console.WriteLine($"FAIL {failed.Id}: {failed.Error ?? failed.Answer}");

return report.Passed == report.Total ? 0 : 1;

sealed record LoginResponse(string Token);
sealed record AskResponse(string Answer, bool Refused, IReadOnlyList<JsonElement> Citations);
sealed record CaseResult(string Id, string Category, bool RequestSucceeded, bool Refused, bool ExpectedRefusal,
    bool ExpectedTextMatched, bool RefusalMatched, bool CitationMatched, int CitationCount, string? Answer,
    int? StatusCode, string? Error)
{
    public bool Passed => RequestSucceeded && ExpectedTextMatched && RefusalMatched && CitationMatched;
    public static CaseResult HttpFailure(EvalCase test, int status, string error) =>
        new(test.Id, test.Category.ToString(), false, false, test.ExpectRefusal, false, false, false, 0, null, status, error);
    public static CaseResult Exception(EvalCase test, string error) =>
        new(test.Id, test.Category.ToString(), false, false, test.ExpectRefusal, false, false, false, 0, null, null, error);
}
sealed record EvaluationReport(DateTime GeneratedAtUtc, string BaseUrl, int Total, int Passed,
    decimal AnswerHitRate, decimal RefusalCorrectness, decimal CitationRate, IReadOnlyList<CaseResult> Cases)
{
    public static EvaluationReport Create(string baseUrl, IReadOnlyList<CaseResult> results) => new(
        DateTime.UtcNow, baseUrl, results.Count, results.Count(x => x.Passed),
        Rate(results.Count(x => x.ExpectedTextMatched), results.Count),
        Rate(results.Count(x => x.RefusalMatched), results.Count),
        Rate(results.Count(x => x.CitationMatched), results.Count), results);
    private static decimal Rate(int numerator, int denominator) => denominator == 0 ? 0 : decimal.Round((decimal)numerator / denominator, 4);
}
sealed record EvaluationOptions(string BaseUrl, string Username, string Password, string? ReportPath, HashSet<EvalCategory> Categories)
{
    public static EvaluationOptions Parse(string[] args)
    {
        var baseUrl = "http://localhost:5028"; var username = "adjuster"; var password = "Adjuster#2026-local-only"; string? report = null;
        var categories = new HashSet<EvalCategory>();
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            var value = i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {option}.");
            switch (option)
            {
                case "--base-url": baseUrl = value; break;
                case "--username": username = value; break;
                case "--password": password = value; break;
                case "--report": report = value; break;
                case "--category" when Enum.TryParse<EvalCategory>(value, true, out var category): categories.Add(category); break;
                default: throw new ArgumentException($"Unknown option {option}. Use --base-url, --username, --password, --report, or --category.");
            }
        }
        return new EvaluationOptions(baseUrl, username, password, report, categories);
    }
}
