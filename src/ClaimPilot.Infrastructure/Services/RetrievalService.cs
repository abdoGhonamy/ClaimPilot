using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Retrieval;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Exceptions;
using ClaimPilot.Domain.ValueObjects;
using ClaimPilot.Infrastructure.Data;

namespace ClaimPilot.Infrastructure.Services;

/// <summary>
/// Hybrid retrieval: dense vectors (pgvector cosine distance, HNSW-backed) fused
/// with keyword (ILIKE full-text) retrieval using Reciprocal Rank Fusion.
/// Version filtering is applied BEFORE any retrieval — cross-version leakage is
/// impossible because every query is scoped to one pinned PolicyVersionId.
/// </summary>
public sealed class RetrievalService : IRetrievalService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IPolicyRepository _policies;
    private readonly IUsageTracker _usage;
    private readonly ILogger<RetrievalService> _logger;

    private const string ChunkColumns =
        "\"Id\", \"PolicyVersionId\", \"Content\", \"Section\", \"Clause\", \"Page\", \"Metadata\", \"Embedding\", \"ContentHash\", \"TokenCount\", \"CreatedAt\"";

    public RetrievalService(
        AppDbContext db,
        IEmbeddingProvider embeddings,
        IPolicyRepository policies,
        IUsageTracker usage,
        ILogger<RetrievalService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _policies = policies;
        _usage = usage;
        _logger = logger;
    }

    public async Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        var version = await _db.PolicyVersions
            .Include(v => v.Policy)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == query.PolicyVersionId && v.PolicyId == query.PolicyId, ct)
            ?? throw new DomainException($"Policy version {query.PolicyVersionId} not found for policy {query.PolicyId}.");

        IReadOnlyList<PolicyChunk> dense = Array.Empty<PolicyChunk>();
        var denseFailed = false;
        try
        {
            var vector = await _embeddings.EmbedAsync(query.Question, ct);
            await _usage.RecordAsync(new UsageRecord("retrieval", vector.Provider, vector.Model,
                vector.InputTokens ?? 0, 0, vector.InputTokens ?? 0, 0m, DateTime.UtcNow), ct);
            dense = await DenseRetrieveAsync(query, version, vector.Vector, ct);
        }
        catch (Exception ex)
        {
            denseFailed = true;
            _logger.LogWarning(ex, "Dense retrieval unavailable; falling back to keyword-only.");
        }

        var keyword = await KeywordRetrieveAsync(query, version, ct);
        if (denseFailed && keyword.Count == 0)
        {
            return new RetrievalResult
            {
                Chunks = Array.Empty<RetrievedChunk>(),
                Sufficient = false,
                InsufficiencyReason = "No policy chunks matched the question.",
                RetrievalTraceId = Guid.NewGuid().ToString()
            };
        }

        var fused = Fuse(dense, keyword).Take(query.TopK).ToList();

        var chunks = fused.Select(r => new RetrievedChunk
        {
            ChunkId = r.Chunk.Id.ToString(),
            PolicyId = query.PolicyId.ToString(),
            PolicyNumber = version.Policy.PolicyNumber,
            Version = version.Version,
            EffectiveDate = version.EffectiveDate,
            Section = r.Chunk.Section,
            Clause = r.Chunk.Clause,
            Page = r.Chunk.Page,
            Text = r.Chunk.Content,
            Citation = new Citation
            {
                ChunkId = r.Chunk.Id.ToString(),
                PolicyId = query.PolicyId.ToString(),
                Version = version.Version,
                Section = r.Chunk.Section,
                Clause = r.Chunk.Clause,
                Page = r.Chunk.Page,
                TextExcerpt = Truncate(r.Chunk.Content, 140),
                Source = $"{version.Policy.PolicyNumber} v{version.Version}"
            },
            Score = (float)r.Score,
            DenseRank = r.DenseRank,
            KeywordRank = r.KeywordRank
        }).ToList();

        return new RetrievalResult
        {
            Chunks = chunks,
            Sufficient = chunks.Count > 0,
            InsufficiencyReason = chunks.Count == 0 ? "No policy chunks matched the question." : null,
            RetrievalTraceId = Guid.NewGuid().ToString()
        };
    }

    public async Task<AskResult> AskAsync(string question, string policyNumber, DateTime? incidentDate, CancellationToken ct)
    {
        var policy = await _policies.GetByPolicyNumberAsync(policyNumber, ct)
            ?? throw new DomainException($"Policy '{policyNumber}' not found.");

        PolicyVersion selected;
        if (incidentDate.HasValue)
        {
            selected = await _policies.GetApplicableVersionAsync(policy.Id, incidentDate.Value, ct)
                ?? throw new PolicyVersionNotFoundException(policyNumber, incidentDate.Value);
        }
        else
        {
            var versions = await _policies.GetVersionsAsync(policy.Id, ct);
            selected = versions.OrderByDescending(v => v.EffectiveDate).First();
        }

        var result = await RetrieveAsync(new RetrievalQuery
        {
            PolicyId = policy.Id,
            PolicyVersionId = selected.Id,
            Question = question,
            TopK = 6
        }, ct);

        if (!result.Sufficient)
        {
            return new AskResult
            {
                Answer = "Not enough information in the policy corpus to determine this.",
                Citations = result.Chunks,
                Refused = true,
                RefusalReason = "Corpus lacks information to answer the question.",
                RunId = result.RetrievalTraceId
            };
        }

        return new AskResult
        {
            Answer = $"See citations for version {selected.Version} ({selected.EffectiveDate:yyyy-MM-dd}).",
            Citations = result.Chunks,
            RunId = result.RetrievalTraceId
        };
    }

    private async Task<IReadOnlyList<PolicyChunk>> DenseRetrieveAsync(
        RetrievalQuery query, PolicyVersion version, float[] vector, CancellationToken ct)
    {
        var sql = $"SELECT {ChunkColumns} FROM \"PolicyChunks\" WHERE \"PolicyVersionId\" = @versionId ORDER BY \"Embedding\" <=> @vector LIMIT @take";

        var versionParam = new NpgsqlParameter("versionId", version.Id);
        var vectorParam = new NpgsqlParameter("vector", new Pgvector.Vector(vector));
        var takeParam = new NpgsqlParameter("take", query.TopK + 3);

        return await _db.PolicyChunks
            .FromSqlRaw(sql, versionParam, vectorParam, takeParam)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<PolicyChunk>> KeywordRetrieveAsync(
        RetrievalQuery query, PolicyVersion version, CancellationToken ct)
    {
        var terms = ExtractTerms(query.Question);
        if (terms.Count == 0)
            return Array.Empty<PolicyChunk>();

        var conditions = string.Join(" OR ", terms.Select((_, i) => $"\"Content\" ILIKE @p{i}"));
        var sql = $"SELECT {ChunkColumns} FROM \"PolicyChunks\" WHERE \"PolicyVersionId\" = @versionId AND ({conditions})";

        var parameters = new List<NpgsqlParameter> { new("versionId", version.Id) };
        for (var i = 0; i < terms.Count; i++)
            parameters.Add(new NpgsqlParameter($"p{i}", $"%{terms[i]}%"));

        return await _db.PolicyChunks
            .FromSqlRaw(sql, parameters.ToArray())
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private static List<RetrievedScored> Fuse(
        IReadOnlyList<PolicyChunk> dense, IReadOnlyList<PolicyChunk> keyword)
    {
        const double k = 60.0;
        var scores = new Dictionary<Guid, (int DenseRank, int KeywordRank, double Score)>();

        for (var i = 0; i < dense.Count; i++)
            scores[dense[i].Id] = (i + 1, -1, 1.0 / (k + i + 1));

        for (var i = 0; i < keyword.Count; i++)
        {
            var id = keyword[i].Id;
            if (scores.TryGetValue(id, out var existing))
                scores[id] = (existing.DenseRank, i + 1, existing.Score + 1.0 / (k + i + 1));
            else
                scores[id] = (-1, i + 1, 1.0 / (k + i + 1));
        }

        var byId = dense.Concat(keyword).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        return scores
            .OrderByDescending(pair => pair.Value.Score)
            .Select(pair => new RetrievedScored(byId[pair.Key], pair.Value.DenseRank, pair.Value.KeywordRank,
                pair.Value.Score * 10.0))
            .ToList();
    }

    private static List<string> ExtractTerms(string question)
    {
        var noise = new HashSet<string>
        {
            "what", "is", "the", "my", "for", "of", "and", "to", "a", "an", "in", "on", "claim",
            "claims", "coverage", "policy", "amount", "limit", "how", "much", "does", "do", "i", "can"
        };
        return question
            .Split(' ', ',', ';', '.', '?', '!', '-', '_')
            .Select(w => w.Trim().TrimEnd('s'))
            .Where(w => w.Length > 3 && !noise.Contains(w))
            .Distinct()
            .Take(6)
            .ToList();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}

internal sealed record RetrievedScored(
    PolicyChunk Chunk, int DenseRank, int KeywordRank, double Score);