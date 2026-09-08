using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Ollama embedding provider using nomic-embed-text (or configured model).
/// Embeddings are produced locally; usage is forwarded for accounting.
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;

    public OllamaEmbeddingProvider(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaEmbeddingProvider> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl);
        _http.Timeout = options.Value.Timeout;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "ollama";
    public string ModelName => _options.EmbeddingModel;

    public event UsageRecordedHandler? UsageRecorded;

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken ct)
        => EmbedOneAsync(text, ct);

    public Task<EmbeddingResult[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
        => EmbedManyAsync(texts, ct);

    private async Task<EmbeddingResult> EmbedOneAsync(string text, CancellationToken ct)
    {
        var request = new { model = _options.EmbeddingModel, prompt = text };
        using var response = await _http.PostAsJsonAsync("/api/embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var vector = body.TryGetProperty("embedding", out var embedding)
            ? embedding.EnumerateArray().Select(v => v.GetSingle()).ToArray()
            : Array.Empty<float>();

        var promptTokens = body.TryGetProperty("prompt_eval_count", out var pec) ? pec.GetInt32() : (int?)null;
        RaiseUsage(promptTokens ?? 0, ct);

        return new EmbeddingResult(vector, promptTokens, ModelName, ProviderName);
    }

    private async Task<EmbeddingResult[]> EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var results = new EmbeddingResult[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            results[i] = await EmbedOneAsync(texts[i], ct);
        }
        return results;
    }

    private void RaiseUsage(int input, CancellationToken ct)
    {
        var handler = UsageRecorded;
        if (handler is null) return;

        var record = new UsageRecord("embedding", ProviderName, ModelName, input, 0, input, 0m, DateTime.UtcNow);
        handler(record, ct).GetAwaiter().GetResult();
    }
}