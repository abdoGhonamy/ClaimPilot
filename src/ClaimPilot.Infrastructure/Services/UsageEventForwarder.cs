using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Forwarder that subscribes to LLM/embedding provider usage events and persists
/// them for cost accounting. Provider-independent.
/// </summary>
public sealed class UsageEventForwarder : IHostedService, IDisposable
{
    private readonly IEnumerable<Application.Interfaces.AI.ILLMProvider> _llmProviders;
    private readonly IEnumerable<Application.Interfaces.AI.IEmbeddingProvider> _embeddingProviders;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UsageEventForwarder> _logger;

    public UsageEventForwarder(
        IEnumerable<Application.Interfaces.AI.ILLMProvider> llmProviders,
        IEnumerable<Application.Interfaces.AI.IEmbeddingProvider> embeddingProviders,
        IServiceScopeFactory scopeFactory,
        ILogger<UsageEventForwarder> logger)
    {
        _llmProviders = llmProviders;
        _embeddingProviders = embeddingProviders;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in _llmProviders)
            provider.UsageRecorded += OnUsage;
        foreach (var provider in _embeddingProviders)
            provider.UsageRecorded += OnUsage;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in _llmProviders)
            provider.UsageRecorded -= OnUsage;
        foreach (var provider in _embeddingProviders)
            provider.UsageRecorded -= OnUsage;
        return Task.CompletedTask;
    }

    private async Task OnUsage(Application.Interfaces.AI.UsageRecord record, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<Application.Interfaces.Repositories.IUsageRepository>();
            await writer.AddAsync(new Application.Interfaces.Repositories.UsageRecordEntry(
                record.Scope, record.Provider, record.Model, record.InputTokens, record.OutputTokens,
                record.TotalTokens, record.EstimatedCostUsd, record.TimestampUtc, record.RunId, record.CorrelationId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist usage record.");
        }
    }

    public void Dispose() { }
}