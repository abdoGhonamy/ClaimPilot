using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Infrastructure.Services;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.2";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Ollama LLM provider. Streaming completion supported. Usage is recorded and
/// forwarded to subscribers for cost accounting (local usage is cost zero).
/// </summary>
public sealed class OllamaLLMProvider : ILLMProvider
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaLLMProvider> _logger;

    public OllamaLLMProvider(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaLLMProvider> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl);
        _http.Timeout = options.Value.Timeout;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "ollama";
    public string ModelName => _options.ChatModel;

    public event UsageRecordedHandler? UsageRecorded;

    public async Task<LLMResult> CompleteAsync(
        string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        var messages = BuildMessages(systemPrompt, userContent, history);

        var request = new
        {
            model = _options.ChatModel,
            messages,
            stream = false,
            options = new { num_predict = 2048, temperature = 0.1 }
        };

        using var response = await _http.PostAsJsonAsync("/api/chat", request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        var text = body.TryGetProperty("message", out var message) &&
                   message.TryGetProperty("content", out var content)
            ? content.GetString() ?? string.Empty
            : string.Empty;

        var promptTokens = body.TryGetProperty("prompt_eval_count", out var pec) ? pec.GetInt32() : (int?)null;
        var outputTokens = body.TryGetProperty("eval_count", out var ec) ? ec.GetInt32() : (int?)null;

        RaiseUsage(promptTokens ?? 0, outputTokens ?? 0, ct);

        return new LLMResult(text, promptTokens, outputTokens, ModelName, ProviderName);
    }

    public async IAsyncEnumerable<LLMResult> StreamCompleteAsync(
        string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = BuildMessages(systemPrompt, userContent, history);
        var request = new
        {
            model = _options.ChatModel,
            messages,
            stream = true,
            options = new { temperature = 0.1 }
        };

        using var response = await _http.PostAsJsonAsync("/api/chat", request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var outputTokens = 0;
        var promptTokens = 0;
        var buffer = new StringBuilder();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var (parsed, fragment) = TryParseChunk(line, ref promptTokens, ref outputTokens);
            if (!parsed)
            {
                _logger.LogWarning("Failed to parse a streaming chunk from Ollama.");
                continue;
            }

            if (fragment.Length > 0)
            {
                buffer.Append(fragment);
                yield return new LLMResult(fragment, null, null, ModelName, ProviderName);
            }
        }

        RaiseUsage(promptTokens, outputTokens, ct);
        yield return new LLMResult(buffer.ToString(), promptTokens, outputTokens, ModelName, ProviderName);
    }

    private static (bool Ok, string Fragment) TryParseChunk(string line, ref int promptTokens, ref int outputTokens)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("prompt_eval_count", out var pec)) promptTokens = pec.GetInt32();
            if (root.TryGetProperty("eval_count", out var ec)) outputTokens = ec.GetInt32();

            if (root.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                return (true, content.GetString() ?? string.Empty);
            }

            return (true, string.Empty);
        }
        catch (JsonException)
        {
            return (false, string.Empty);
        }
    }

    private List<object> BuildMessages(string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        if (history is not null)
        {
            foreach (var m in history)
                messages.Add(new { role = m.Role, content = m.Content });
        }
        messages.Add(new { role = "user", content = userContent });
        return messages;
    }

    private void RaiseUsage(int input, int output, CancellationToken ct)
    {
        var handler = UsageRecorded;
        if (handler is null) return;

        // Local Ollama usage has zero cost.
        var record = new UsageRecord("llm", ProviderName, ModelName, input, output, input + output, 0m,
            DateTime.UtcNow);
        handler(record, ct).GetAwaiter().GetResult();
    }
}