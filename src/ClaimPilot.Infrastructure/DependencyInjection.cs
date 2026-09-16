using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using StackExchange.Redis;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Documents;
using ClaimPilot.Application.Interfaces.Orchestration;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces.Retrieval;
using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Application.Interfaces.Trace;
using ClaimPilot.Application.Services;
using ClaimPilot.Infrastructure.Data;
using ClaimPilot.Infrastructure.Data.Seed;
using ClaimPilot.Infrastructure.Repositories;
using ClaimPilot.Infrastructure.Services;
using ClaimPilot.Infrastructure.Services.Documents;

namespace ClaimPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=claimpilot;Username=claimpilot;Password=claimpilot";

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.UseVector();
            }));

        return services
            .AddOllama(configuration)
            .AddRepositories()
            .AddDocumentServices()
            .AddRedis(configuration)
            .AddBackgroundWorkers();
    }

    private static IServiceCollection AddOllama(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));

        services.AddHttpClient<OllamaLLMProvider>();
        services.AddHttpClient<OllamaEmbeddingProvider>();
        services.AddSingleton<DeterministicLLMProvider>();
        services.AddSingleton<ILLMProvider>(sp =>
            string.Equals(configuration["AI:Provider"], "Deterministic", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<DeterministicLLMProvider>()
                : sp.GetRequiredService<OllamaLLMProvider>());
        services.AddSingleton<IEmbeddingProvider>(sp => sp.GetRequiredService<OllamaEmbeddingProvider>());

        services.AddHostedService<UsageEventForwarder>();
        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IChunkRepository, ChunkRepository>();
        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<ITraceRepository, TraceRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ITraceService, TraceService>();
        services.AddScoped<IUsageTracker, UsageTracker>();
        services.AddScoped<IToolTraceWriter, ToolTraceWriter>();

        services.AddScoped<IRetrievalService, RetrievalService>();
        services.AddScoped<IToolRegistry, ToolRegistryService>();

        services.AddScoped<IReviewDataProvider>(sp =>
            new ReviewDataProvider(sp.GetRequiredService<IApprovalRepository>()));
        services.AddScoped<IRunTraceViewBuilder, RunTraceViewBuilder>();
        services.AddScoped<IApprovalQueueReader, ReviewQueueReader>();
        services.AddScoped<DemoDataSeeder>();

        return services;
    }

    private static IServiceCollection AddDocumentServices(this IServiceCollection services)
    {
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IChunkingStrategy, StructuralChunkingStrategy>();
        services.AddScoped<IFileTextExtractor, PdfTextExtractor>();
        services.AddScoped<IFileTextExtractor, MarkdownTextExtractor>();
        services.AddScoped<IFileTextExtractor, DocxTextExtractor>();
        return services;
    }

    private static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection("Redis"));
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            try
            {
                return ConnectionMultiplexer.Connect(options.ConnectionString + $",abortConnect=false&connectTimeout={options.Timeout.TotalMilliseconds}");
            }
            catch
            {
                // Redis is optional for local runs; lazy reconnect handles recovery.
                return ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
            }
        });
        services.AddSingleton<IRedisCache, RedisCache>();
        return services;
    }

    private static IServiceCollection AddBackgroundWorkers(this IServiceCollection services)
    {
        services.AddHostedService<SlaEscalationWorker>();
        return services;
    }
}

/// <summary>Reads approval items for statistics from the repository.</summary>
internal sealed class ReviewDataProvider : IReviewDataProvider
{
    private readonly IApprovalRepository _approvals;

    public ReviewDataProvider(IApprovalRepository approvals)
    {
        _approvals = approvals;
    }

    public async Task<IReadOnlyList<StatisticRow>> GetApprovalItemsAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var items = await _approvals.QueryAsync(null, null, null, ct);
        return items
            .Where(i => (!from.HasValue || i.CreatedAt >= from) && (!to.HasValue || i.CreatedAt <= to))
            .Select(i => new StatisticRow(i.Status, i.CreatedAt, i.ReviewedAt, i.SLADeadline, i.AssignedTo?.ToString()))
            .ToList();
    }
}
