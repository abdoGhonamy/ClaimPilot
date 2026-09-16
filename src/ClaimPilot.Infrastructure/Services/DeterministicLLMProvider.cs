using ClaimPilot.Application.Interfaces.AI;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Offline, dependency-free provider for demos, tests, and graceful provider
/// fallback. It deliberately refuses rather than fabricating a decision.
/// </summary>
public sealed class DeterministicLLMProvider : ILLMProvider
{
    public string ProviderName => "deterministic";
    public string ModelName => "safe-fallback-v1";
    public event UsageRecordedHandler? UsageRecorded;

    public async Task<LLMResult> CompleteAsync(string systemPrompt, string userContent,
        IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        var result = new LLMResult("Not enough information in the policy corpus to determine this.", 0, 0, ModelName, ProviderName);
        if (UsageRecorded is not null)
            await UsageRecorded(new UsageRecord("completion", ProviderName, ModelName, 0, 0, 0, 0m, DateTime.UtcNow), ct);
        return result;
    }

    public async IAsyncEnumerable<LLMResult> StreamCompleteAsync(string systemPrompt, string userContent,
        IReadOnlyList<ChatMessage>? history, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        yield return await CompleteAsync(systemPrompt, userContent, history, ct);
    }
}
