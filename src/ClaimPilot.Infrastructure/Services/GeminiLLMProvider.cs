using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>Configuration for the hosted Gemini chat fallback.</summary>
public sealed class GeminiOptions
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "gemini-3.6-flash";
    public string EmbeddingModel { get; set; } = "gemini-embedding-2";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Gemini REST chat provider. It deliberately has no embedding implementation: existing
/// pgvector embeddings remain compatible when chat traffic fails over from Ollama.
/// </summary>
public sealed class GeminiLLMProvider : ILLMProvider
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiLLMProvider(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = _options.Timeout;
    }

    public string ProviderName => "gemini";
    public string ModelName => _options.ChatModel;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public event UsageRecordedHandler? UsageRecorded;

    public async Task<LLMResult> CompleteAsync(
        string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        EnsureConfigured();
        using var request = CreateRequest($"v1beta/models/{ModelName}:generateContent", systemPrompt, userContent, history);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Gemini returned an empty response.");
        var (text, inputTokens, outputTokens) = ParseResponse(body.RootElement);
        await RaiseUsageAsync(inputTokens, outputTokens, ct);
        return new LLMResult(text, inputTokens, outputTokens, ModelName, ProviderName);
    }

    public async IAsyncEnumerable<LLMResult> StreamCompleteAsync(
        string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureConfigured();
        using var request = CreateRequest($"v1beta/models/{ModelName}:streamGenerateContent?alt=sse", systemPrompt, userContent, history);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var inputTokens = 0;
        var outputTokens = 0;
        var accumulated = new StringBuilder();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            using var document = JsonDocument.Parse(line[6..]);
            var (text, prompt, output) = ParseResponse(document.RootElement);
            inputTokens = prompt ?? inputTokens;
            outputTokens = output ?? outputTokens;
            if (text.Length == 0) continue;

            accumulated.Append(text);
            yield return new LLMResult(text, null, null, ModelName, ProviderName);
        }

        await RaiseUsageAsync(inputTokens, outputTokens, ct);
        yield return new LLMResult(accumulated.ToString(), inputTokens, outputTokens, ModelName, ProviderName);
    }

    private HttpRequestMessage CreateRequest(string path, string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history)
    {
        var contents = new List<object>();
        if (history is not null)
        {
            foreach (var message in history)
            {
                var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
                contents.Add(new { role, parts = new[] { new { text = message.Content } } });
            }
        }
        contents.Add(new { role = "user", parts = new[] { new { text = userContent } } });

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents,
                generationConfig = new { temperature = 0.1, maxOutputTokens = 2048 }
            })
        };
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        return request;
    }

    private static (string Text, int? InputTokens, int? OutputTokens) ParseResponse(JsonElement root)
    {
        var text = new StringBuilder();
        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var value)) text.Append(value.GetString());
        }

        int? prompt = null;
        int? output = null;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var promptValue)) prompt = promptValue.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var outputValue)) output = outputValue.GetInt32();
        }
        return (text.ToString(), prompt, output);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini fallback is not configured. Set Gemini__ApiKey.");
    }

    private async Task RaiseUsageAsync(int? input, int? output, CancellationToken ct)
    {
        var handler = UsageRecorded;
        if (handler is not null)
            await handler(new UsageRecord("llm", ProviderName, ModelName, input ?? 0, output ?? 0,
                (input ?? 0) + (output ?? 0), 0m, DateTime.UtcNow), ct);
    }
}
