using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClaimPilot.Evaluation;
using ClaimPilot.Infrastructure.Data.Seed;

var options = EvaluationOptions.Parse(args);
using var client = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
HttpResponseMessage login;
try
{
    login = await client.PostAsJsonAsync("/api/auth/login", new { username = options.Username, password = options.Password });
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not connect to ClaimPilot at {options.BaseUrl}: {ex.Message}");
    Console.Error.WriteLine("Start the Docker stack with 'docker compose up -d --build', then use --base-url http://localhost:8080.");
    Console.Error.WriteLine("If you started the API directly with dotnet run, use --base-url http://localhost:5028 instead.");
    return 2;
}
login.EnsureSuccessStatusCode();
var session = await login.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new InvalidOperationException("Login returned no token.");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
return options.Mode == EvaluationMode.Adjudication
    ? await RunAdjudicationEvaluationAsync(client, options)
    : await RunAskEvaluationAsync(client, options);

static async Task<int> RunAskEvaluationAsync(HttpClient client, EvaluationOptions options)
{
    var cases = options.Categories.Count == 0 ? EvaluationCorpus.All : EvaluationCorpus.All.Where(c => options.Categories.Contains(c.Category)).ToList();
    var results = new List<AskCaseResult>();
    foreach (var test in cases)
    {
        try
        {
            var response = await client.PostAsJsonAsync("/api/ask", new { question = test.Question, policyNumber = test.PolicyNumber, incidentDate = test.IncidentDate });
            if (!response.IsSuccessStatusCode) { results.Add(AskCaseResult.HttpFailure(test, (int)response.StatusCode, await response.Content.ReadAsStringAsync())); continue; }
            var answer = await response.Content.ReadFromJsonAsync<AskResponse>();
            if (answer is null) { results.Add(AskCaseResult.HttpFailure(test, 200, "Empty JSON response.")); continue; }
            var expected = test.ExpectFullMatch ?? test.ExpectedSubstring;
            var textMatched = test.ExpectRefusal ? answer.Refused && answer.Answer.Equals("Not enough information in the policy corpus to determine this.", StringComparison.Ordinal) : answer.Answer.Contains(expected, StringComparison.OrdinalIgnoreCase);
            results.Add(new AskCaseResult(test.Id, test.Category.ToString(), true, answer.Refused, test.ExpectRefusal, textMatched, answer.Refused == test.ExpectRefusal, test.ExpectRefusal || answer.Citations.Count > 0, answer.Citations.Count, answer.Answer, null, null));
        }
        catch (Exception ex) { results.Add(AskCaseResult.Exception(test, ex.Message)); }
    }
    var report = AskEvaluationReport.Create(options.BaseUrl, results);
    await WriteReportAsync(options.ReportPath ?? DefaultReportPath("ask"), report);
    Console.WriteLine($"Ask evaluation: {report.Passed}/{report.Total} passed ({report.AnswerHitRate:P1} answer hit rate)");
    Console.WriteLine($"Refusal correctness: {report.RefusalCorrectness:P1}; citation rate: {report.CitationRate:P1}");
    return report.Passed == report.Total ? 0 : 1;
}

static async Task<int> RunAdjudicationEvaluationAsync(HttpClient client, EvaluationOptions options)
{
    var results = new List<AdjudicationCaseResult>();
    foreach (var test in AdjudicationEvaluationCorpus.All)
    {
        try
        {
            var created = await client.PostAsJsonAsync("/api/claims", new { policyNumber = test.PolicyNumber, incidentDate = DateTime.SpecifyKind(test.IncidentDate, DateTimeKind.Utc), claimAmount = test.ClaimAmount, description = test.Description });
            if (!created.IsSuccessStatusCode) { results.Add(AdjudicationCaseResult.HttpFailure(test, "create_claim", (int)created.StatusCode, await created.Content.ReadAsStringAsync())); continue; }
            var claim = await created.Content.ReadFromJsonAsync<ClaimResponse>() ?? throw new InvalidOperationException("Create claim returned no JSON.");
            var events = await ReadSseAsync(await client.PostAsync($"/api/claims/{claim.Id}/adjudicate", null));
            results.Add(AdjudicationCaseResult.Evaluate(test, events));
        }
        catch (Exception ex) { results.Add(AdjudicationCaseResult.Exception(test, ex.Message)); }
    }
    var report = AdjudicationEvaluationReport.Create(options.BaseUrl, results);
    await WriteReportAsync(options.ReportPath ?? DefaultReportPath("adjudication"), report);
    Console.WriteLine($"Adjudication evaluation: {report.Passed}/{report.Total} passed");
    Console.WriteLine($"Coverage Matcher: {report.CoverageRate:P1}; Exclusion Analyst: {report.ExclusionRate:P1}; Anomaly Detector: {report.AnomalyRate:P1}");
    foreach (var failed in results.Where(x => !x.Passed)) Console.WriteLine($"FAIL {failed.Id}: {failed.Error ?? failed.FailureSummary}");
    return report.Passed == report.Total ? 0 : 1;
}

static async Task<List<SseEvent>> ReadSseAsync(HttpResponseMessage response)
{
    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Adjudication returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    var body = await response.Content.ReadAsStringAsync(); var events = new List<SseEvent>();
    foreach (var frame in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
    {
        var name = frame.Split('\n').FirstOrDefault(x => x.StartsWith("event: ", StringComparison.Ordinal))?[7..] ?? "event";
        var json = frame.Split('\n').FirstOrDefault(x => x.StartsWith("data: ", StringComparison.Ordinal))?[6..];
        if (string.IsNullOrWhiteSpace(json)) continue;
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        events.Add(new SseEvent(name, root.TryGetProperty("event_type", out var t) ? t.GetString() ?? name : name,
            root.TryGetProperty("agent", out var a) ? a.GetString() : null,
            root.TryGetProperty("payload", out var p) ? p.GetString() : null));
    }
    return events;
}
static async Task WriteReportAsync(string outputPath, object report) { Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? "."); await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine($"Report: {Path.GetFullPath(outputPath)}"); }
static string DefaultReportPath(string mode) => Path.Combine("artifacts", $"evaluation-{mode}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");

sealed record LoginResponse(string Token); sealed record ClaimResponse(Guid Id); sealed record AskResponse(string Answer, bool Refused, IReadOnlyList<JsonElement> Citations); sealed record SseEvent(string Name, string Type, string? Agent, string? Payload);
sealed record AskCaseResult(string Id, string Category, bool RequestSucceeded, bool Refused, bool ExpectedRefusal, bool ExpectedTextMatched, bool RefusalMatched, bool CitationMatched, int CitationCount, string? Answer, int? StatusCode, string? Error)
{ public bool Passed => RequestSucceeded && ExpectedTextMatched && RefusalMatched && CitationMatched; public static AskCaseResult HttpFailure(EvalCase t, int s, string e) => new(t.Id,t.Category.ToString(),false,false,t.ExpectRefusal,false,false,false,0,null,s,e); public static AskCaseResult Exception(EvalCase t,string e) => new(t.Id,t.Category.ToString(),false,false,t.ExpectRefusal,false,false,false,0,null,null,e); }
sealed record AskEvaluationReport(DateTime GeneratedAtUtc,string BaseUrl,int Total,int Passed,decimal AnswerHitRate,decimal RefusalCorrectness,decimal CitationRate,IReadOnlyList<AskCaseResult> Cases)
{ public static AskEvaluationReport Create(string url,IReadOnlyList<AskCaseResult> r) => new(DateTime.UtcNow,url,r.Count,r.Count(x=>x.Passed),Rate(r.Count(x=>x.ExpectedTextMatched),r.Count),Rate(r.Count(x=>x.RefusalMatched),r.Count),Rate(r.Count(x=>x.CitationMatched),r.Count),r); private static decimal Rate(int n,int d)=>d==0?0:decimal.Round((decimal)n/d,4); }
sealed record AdjudicationCaseResult(string Id,string Name,bool RequestSucceeded,bool CoverageAgentCompleted,bool ExclusionAgentCompleted,bool AnomalyAgentCompleted,bool PolicyVersionMatched,bool ExclusionMatched,bool AnomalyMatched,IReadOnlyList<string> Events,int? StatusCode,string? Error)
{
    public bool Passed => RequestSucceeded && CoverageAgentCompleted && ExclusionAgentCompleted && AnomalyAgentCompleted && PolicyVersionMatched && ExclusionMatched && AnomalyMatched;
    public string FailureSummary => $"coverage={CoverageAgentCompleted}/{PolicyVersionMatched}, exclusion={ExclusionAgentCompleted}/{ExclusionMatched}, anomaly={AnomalyAgentCompleted}/{AnomalyMatched}";
    public static AdjudicationCaseResult Evaluate(AdjudicationEvalCase test,IReadOnlyList<SseEvent> events)
    {
        var done=events.Where(e=>e.Type=="agent_completed").ToList(); var coverage=done.FirstOrDefault(e=>e.Agent=="Coverage Matcher"); var exclusion=done.FirstOrDefault(e=>e.Agent=="Exclusion Analyst"); var anomaly=done.FirstOrDefault(e=>e.Agent=="Anomaly Detector"); var failed=events.Any(e=>e.Name=="error"||e.Type=="error");
        return new(test.Id,test.Name,!failed,coverage is not null,exclusion is not null,anomaly is not null,coverage is not null&&PayloadHasInt(coverage.Payload,"version",test.ExpectedPolicyVersion),test.ExpectedExclusionCode is null||exclusion is not null&&Contains(exclusion.Payload,test.ExpectedExclusionCode),test.ExpectedAnomalyType is null||anomaly is not null&&Contains(anomaly.Payload,test.ExpectedAnomalyType),events.Select(e=>$"{e.Type}:{e.Agent??"system"}").ToList(),null,failed?"Endpoint emitted an error SSE event.":null);
    }
    public static AdjudicationCaseResult HttpFailure(AdjudicationEvalCase t,string stage,int s,string e)=>new(t.Id,t.Name,false,false,false,false,false,false,false,Array.Empty<string>(),s,$"{stage}: {e}"); public static AdjudicationCaseResult Exception(AdjudicationEvalCase t,string e)=>new(t.Id,t.Name,false,false,false,false,false,false,false,Array.Empty<string>(),null,e);
    private static bool Contains(string? value,string expected)=>value?.Contains(expected,StringComparison.OrdinalIgnoreCase)==true; private static bool PayloadHasInt(string? payload,string property,int expected){if(string.IsNullOrWhiteSpace(payload))return false;try{using var d=JsonDocument.Parse(payload);return d.RootElement.TryGetProperty(property,out var v)&&v.TryGetInt32(out var n)&&n==expected;}catch(JsonException){return false;}}
}
sealed record AdjudicationEvaluationReport(DateTime GeneratedAtUtc,string BaseUrl,int Total,int Passed,decimal CoverageRate,decimal ExclusionRate,decimal AnomalyRate,IReadOnlyList<AdjudicationCaseResult> Cases)
{ public static AdjudicationEvaluationReport Create(string url,IReadOnlyList<AdjudicationCaseResult> r)=>new(DateTime.UtcNow,url,r.Count,r.Count(x=>x.Passed),Rate(r.Count(x=>x.CoverageAgentCompleted&&x.PolicyVersionMatched),r.Count),Rate(r.Count(x=>x.ExclusionAgentCompleted&&x.ExclusionMatched),r.Count),Rate(r.Count(x=>x.AnomalyAgentCompleted&&x.AnomalyMatched),r.Count),r); private static decimal Rate(int n,int d)=>d==0?0:decimal.Round((decimal)n/d,4); }
enum EvaluationMode { Ask, Adjudication }
sealed record EvaluationOptions(string BaseUrl,string Username,string Password,string? ReportPath,HashSet<EvalCategory> Categories,EvaluationMode Mode)
{ public static EvaluationOptions Parse(string[] args){var url="http://localhost:8080";var user="adjuster";var password="Adjuster#2026-local-only";string? report=null;var mode=EvaluationMode.Ask;var categories=new HashSet<EvalCategory>();for(var i=0;i<args.Length;i++){var option=args[i];var value=i+1<args.Length?args[++i]:throw new ArgumentException($"Missing value for {option}.");switch(option){case "--base-url":url=value;break;case "--username":user=value;break;case "--password":password=value;break;case "--report":report=value;break;case "--mode" when Enum.TryParse<EvaluationMode>(value,true,out var parsed):mode=parsed;break;case "--category" when Enum.TryParse<EvalCategory>(value,true,out var category):categories.Add(category);break;default:throw new ArgumentException("Use --mode ask|adjudication, --base-url, --username, --password, --report, or --category.");}}if(mode==EvaluationMode.Adjudication&&categories.Count>0)throw new ArgumentException("--category only applies to --mode ask.");return new(url,user,password,report,categories,mode);} }
