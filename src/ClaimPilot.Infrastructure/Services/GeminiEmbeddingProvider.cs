using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>Gemini-only embedding adapter. Output is explicitly 768 dimensions
/// so its provider-specific pgvector index has a stable, documented shape.</summary>
public sealed class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    public GeminiEmbeddingProvider(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http; _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = _options.Timeout;
    }
    public string ProviderName => "gemini";
    public string ModelName => _options.EmbeddingModel;
    public event UsageRecordedHandler? UsageRecorded;
    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Gemini is not configured. Set Gemini__ApiKey.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"v1beta/models/{ModelName}:embedContent")
        {
            Content = JsonContent.Create(new
            {
                content = new { parts = new[] { new { text } } },
                outputDimensionality = 768
            })
        };
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Gemini returned an empty embedding response.");
        var values = body.RootElement.GetProperty("embedding").GetProperty("values")
            .EnumerateArray().Select(v => v.GetSingle()).ToArray();
        if (values.Length != 768) throw new InvalidOperationException($"Gemini returned {values.Length} dimensions; expected 768.");
        if (UsageRecorded is not null)
            await UsageRecorded(new UsageRecord("embedding", ProviderName, ModelName, 0, 0, 0, 0m, DateTime.UtcNow), ct);
        return new EmbeddingResult(values, null, ModelName, ProviderName);
    }
    public async Task<EmbeddingResult[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var results = new EmbeddingResult[texts.Count];
        for (var i = 0; i < texts.Count; i++) results[i] = await EmbedAsync(texts[i], ct);
        return results;
    }
}
