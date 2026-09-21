using System.Threading;

using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>Async-flow local pipeline choice. A request starts with Gemini and can
/// move to Ollama before retrieval; trace writers can read the fallback reason.</summary>
public sealed class AiPipelineContext : IAiPipelineContext
{
    private readonly AsyncLocal<State?> _state = new();
    public AiPipeline Current => _state.Value?.Pipeline ?? AiPipeline.Gemini;
    public string? FallbackReason => _state.Value?.FallbackReason;
    public void Select(AiPipeline pipeline, string? reason = null) =>
        _state.Value = new State(pipeline, reason);
    private sealed record State(AiPipeline Pipeline, string? FallbackReason);
}

public sealed class EmbeddingProviderResolver : IEmbeddingProviderResolver
{
    private readonly GeminiEmbeddingProvider _gemini;
    private readonly OllamaEmbeddingProvider _ollama;
    public EmbeddingProviderResolver(GeminiEmbeddingProvider gemini, OllamaEmbeddingProvider ollama)
        => (_gemini, _ollama) = (gemini, ollama);
    public IEmbeddingProvider Get(AiPipeline pipeline) => pipeline == AiPipeline.Gemini ? _gemini : _ollama;
}
