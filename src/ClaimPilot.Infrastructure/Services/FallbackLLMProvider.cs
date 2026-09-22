using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>Selects Gemini first. Any technical Gemini failure before output moves the
/// complete request context to the local Ollama pipeline.</summary>
public sealed class FallbackLLMProvider : ILLMProvider
{
    private readonly OllamaLLMProvider _ollama;
    private readonly GeminiLLMProvider _gemini;
    private readonly ILogger<FallbackLLMProvider> _logger;
    private readonly IAiPipelineContext _pipeline;

    public FallbackLLMProvider(OllamaLLMProvider ollama, GeminiLLMProvider gemini, IAiPipelineContext pipeline, ILogger<FallbackLLMProvider> logger)
    {
        _ollama = ollama;
        _gemini = gemini;
        _logger = logger;
        _pipeline = pipeline;
        _ollama.UsageRecorded += ForwardUsageAsync;
        _gemini.UsageRecorded += ForwardUsageAsync;
    }

    public string ProviderName => "gemini-with-ollama-fallback";
    public string ModelName => _gemini.ModelName;
    public event UsageRecordedHandler? UsageRecorded;

    public async Task<LLMResult> CompleteAsync(
        string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        try
        {
            if (_pipeline.Current != AiPipeline.Ollama)
                _pipeline.Select(AiPipeline.Gemini);
            return await _gemini.CompleteAsync(systemPrompt, userContent, history, ct);
        }
        catch (Exception ex) when (CanFailOver(ex, ct))
        {
            _logger.LogWarning(ex, "Gemini completion failed; using local Ollama pipeline.");
            _pipeline.Select(AiPipeline.Ollama, ex.GetType().Name);
            return await _ollama.CompleteAsync(systemPrompt, userContent, history, ct);
        }
    }

    public async IAsyncEnumerable<LLMResult> StreamCompleteAsync(
        string systemPrompt, string userContent, IReadOnlyList<ChatMessage>? history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var emitted = false;
        var useGemini = false;
        _pipeline.Select(AiPipeline.Gemini);
        await using var enumerator = _gemini.StreamCompleteAsync(systemPrompt, userContent, history, ct)
            .GetAsyncEnumerator(ct);

        while (true)
        {
            LLMResult result;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                result = enumerator.Current;
            }
            catch (Exception ex) when (!emitted && CanFailOver(ex, ct))
            {
                _logger.LogWarning(ex, "Gemini stream failed before output; using local Ollama pipeline.");
                _pipeline.Select(AiPipeline.Ollama, ex.GetType().Name);
                useGemini = true;
                break;
            }

            emitted = true;
            yield return result;
        }

        if (!useGemini) yield break;
        await foreach (var result in _ollama.StreamCompleteAsync(systemPrompt, userContent, history, ct))
            yield return result;
    }

    private bool CanFailOver(Exception exception, CancellationToken ct)
        => !ct.IsCancellationRequested && exception is not OperationCanceledException;

    private Task ForwardUsageAsync(UsageRecord record, CancellationToken ct)
        => UsageRecorded?.Invoke(record, ct) ?? Task.CompletedTask;
}
