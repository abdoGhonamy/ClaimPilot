using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;

using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Application.Interfaces.Documents;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Services.Documents;

/// <summary>
/// End-to-end ingestion pipeline:
/// validate → extract → clean → detect sections/clauses → structural chunks →
/// embed → store. Idempotent via the ContentHash of each chunk.
/// </summary>
public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IPolicyRepository _policies;
    private readonly IChunkRepository _chunks;
    private readonly IEmbeddingProviderResolver _embeddingProviders;
    private readonly IFileTextExtractor[] _extractors;
    private readonly IChunkingStrategy _chunking;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        IPolicyRepository policies,
        IChunkRepository chunks,
        IEmbeddingProviderResolver embeddingProviders,
        IEnumerable<IFileTextExtractor> extractors,
        IChunkingStrategy chunking,
        ILogger<DocumentIngestionService> logger)
    {
        _policies = policies;
        _chunks = chunks;
        _embeddingProviders = embeddingProviders;
        _extractors = extractors.ToArray();
        _chunking = chunking;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(
        string policyNumber, int version, DateTime effectiveDate, string fileName,
        string contentType, Stream content, CancellationToken ct)
    {
        // Npgsql 9+ requires Kind=Utc when projecting DateTime onto a
        // "timestamp with time zone" column; an Unspecified kind raised
        // DbUpdateException/500 during chunk persistence. Normalize once here
        // so every effective-date write path uses UTC.
        effectiveDate = DateTime.SpecifyKind(effectiveDate, DateTimeKind.Utc);
        var extractor = _extractors.FirstOrDefault(e => e.Supports(contentType, fileName));
        if (extractor is null)
        {
            return new IngestionResult
            {
                DocumentReference = fileName,
                Status = DocumentStatus.Failed,
                Error = $"Unsupported file type: {contentType}"
            };
        }

        var policy = await _policies.GetByPolicyNumberAsync(policyNumber, ct);
        if (policy is null)
        {
            policy = new Policy { PolicyNumber = policyNumber, ProductLine = "Unknown", Name = policyNumber };
            await _policies.AddAsync(policy, ct);
        }

        var policyVersion = await _policies.GetVersionAsync(policy.Id, version, ct);
        if (policyVersion is null)
        {
            policyVersion = new PolicyVersion
            {
                PolicyId = policy.Id,
                Policy = policy,
                Version = version,
                EffectiveDate = effectiveDate
            };
            await _policies.AddVersionAsync(policyVersion, ct);
        }

        try
        {
            var extracted = await extractor.ExtractAsync(content, fileName, ct);
            var cleaned = Clean(extracted.FullText);
            var sections = extracted.Sections
                .Select(s => new ExtractedSection(s.Title, s.Clause, s.Page, Clean(s.Text)))
                .ToList();
            _ = cleaned;

            var payloads = _chunking.Chunk(new ExtractedDocument(cleaned, sections));

            var chunks = new List<PolicyChunk>();
            var skipped = 0;

            foreach (var payload in payloads)
            {
                ct.ThrowIfCancellationRequested();

                var hash = ComputeHash($"{policyNumber}|v{version}|{payload.Section}|{payload.Clause}|{payload.Content}");
                if (await _chunks.ExistsByHashAsync(hash, ct))
                {
                    skipped++;
                    continue;
                }

                chunks.Add(new PolicyChunk
                {
                    PolicyVersionId = policyVersion.Id,
                    PolicyVersion = policyVersion,
                    Content = payload.Content,
                    Section = payload.Section,
                    Clause = payload.Clause,
                    Page = payload.Page,
                    ContentHash = hash
                });
            }

            if (chunks.Count > 0)
            {
                // Each provider owns a distinct vector space. A Gemini outage must not
                // discard the local Ollama index; a later re-index can fill Gemini.
                var texts = chunks.Select(c => $"{c.Section} {c.Clause}\n{c.Content}").ToList();
                await AddProviderEmbeddingsAsync(chunks, texts, AiPipeline.Ollama, required: true, ct);
                await AddProviderEmbeddingsAsync(chunks, texts, AiPipeline.Gemini, required: false, ct);

                await _chunks.AddRangeAsync(chunks, ct);
            }

            return new IngestionResult
            {
                DocumentReference = fileName,
                Status = DocumentStatus.Completed,
                ChunksCreated = chunks.Count,
                ChunksSkipped = skipped
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for {File}", fileName);
            return new IngestionResult
            {
                DocumentReference = fileName,
                Status = DocumentStatus.Failed,
                Error = ex.Message
            };
        }
    }

    private async Task AddProviderEmbeddingsAsync(IReadOnlyList<PolicyChunk> chunks, IReadOnlyList<string> texts,
        AiPipeline pipeline, bool required, CancellationToken ct)
    {
        try
        {
            var provider = _embeddingProviders.Get(pipeline);
            var vectors = await provider.EmbedBatchAsync(texts, ct);
            for (var i = 0; i < chunks.Count; i++)
            {
                chunks[i].Embeddings.Add(new PolicyChunkEmbedding
                {
                    PolicyChunk = chunks[i], Provider = provider.ProviderName, Model = provider.ModelName, Vector = vectors[i].Vector
                });
                if (pipeline == AiPipeline.Ollama) chunks[i].Embedding = vectors[i].Vector; // legacy compatibility
            }
        }
        catch (Exception ex) when (!required)
        {
            _logger.LogWarning(ex, "{Provider} embeddings were deferred; Ollama remains searchable.", pipeline);
        }
    }

    private static string Clean(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\r' || ch == '\t') continue;
            result.Append(ch);
        }
        return result.ToString();
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
