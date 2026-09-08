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
    private readonly IEmbeddingProvider _embeddings;
    private readonly IFileTextExtractor[] _extractors;
    private readonly IChunkingStrategy _chunking;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        IPolicyRepository policies,
        IChunkRepository chunks,
        IEmbeddingProvider embeddings,
        IEnumerable<IFileTextExtractor> extractors,
        IChunkingStrategy chunking,
        ILogger<DocumentIngestionService> logger)
    {
        _policies = policies;
        _chunks = chunks;
        _embeddings = embeddings;
        _extractors = extractors.ToArray();
        _chunking = chunking;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(
        string policyNumber, int version, DateTime effectiveDate, string fileName,
        string contentType, Stream content, CancellationToken ct)
    {
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
                // Embed before persist to avoid saving unsearchable rows.
                var texts = chunks.Select(c => $"{c.Section} {c.Clause}\n{c.Content}").ToList();
                var vectors = await _embeddings.EmbedBatchAsync(texts, ct);
                for (var i = 0; i < chunks.Count; i++)
                    chunks[i].Embedding = vectors[i].Vector;

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