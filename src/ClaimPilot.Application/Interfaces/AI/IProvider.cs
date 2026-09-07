namespace ClaimPilot.Application.Interfaces.AI;

/// <summary>
/// Provider-independent record of a usage event for cost accounting.
/// Local (Ollama) usage may be recorded with zero cost.
/// </summary>
public sealed record UsageRecord(
    string Scope,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    decimal EstimatedCostUsd,
    DateTime TimestampUtc,
    string? RunId = null,
    string? CorrelationId = null);

/// <summary>Result of a single LLM completion (text + usage).</summary>
public sealed record LLMResult(
    string Text,
    int? InputTokens,
    int? OutputTokens,
    string Model,
    string Provider);

/// <summary>Embedding generation result.</summary>
public sealed record EmbeddingResult(
    float[] Vector,
    int? InputTokens,
    string Model,
    string Provider);

public sealed record ChatMessage(string Role, string Content);

public delegate Task UsageRecordedHandler(UsageRecord record, CancellationToken ct);

/// <summary>
/// Abstraction over a local/remote chat LLM. Implementations live in Infrastructure.
/// Providers are replaceable; Application never references Ollama directly.
/// </summary>
public interface ILLMProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    Task<LLMResult> CompleteAsync(
        string systemPrompt,
        string userContent,
        IReadOnlyList<ChatMessage>? history,
        CancellationToken ct);

    IAsyncEnumerable<LLMResult> StreamCompleteAsync(
        string systemPrompt,
        string userContent,
        IReadOnlyList<ChatMessage>? history,
        CancellationToken ct);

    event UsageRecordedHandler? UsageRecorded;
}

/// <summary>
/// Abstraction over an embedding model. Implementations live in Infrastructure.
/// </summary>
public interface IEmbeddingProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken ct);
    Task<EmbeddingResult[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct);

    event UsageRecordedHandler? UsageRecorded;
}