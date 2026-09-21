using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ClaimPilot.Application.Interfaces.AI;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Infrastructure.Data;

namespace ClaimPilot.Infrastructure.Data.Seed;

/// <summary>
/// Seeds the synthetic policy corpus (15 wordings / 5 product lines), coverage
/// tables, exclusions, embedded chunks, and demo claims. Idempotent: it does
/// nothing when the corpus is already present.
/// </summary>
public sealed class DemoDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingProviderResolver _embeddingProviders;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(AppDbContext db, IEmbeddingProviderResolver embeddingProviders, ILogger<DemoDataSeeder> logger)
    {
        _db = db;
        _embeddingProviders = embeddingProviders;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await _db.Policies.AnyAsync(ct))
            return;

        var policies = new Dictionary<string, Policy>();
        var versions = new Dictionary<(string PolicyNumber, int Version), PolicyVersion>();
        var ollamaEmbeddings = _embeddingProviders.Get(AiPipeline.Ollama);
        var embeddingEnabled = await TryProbeEmbeddingAsync(ollamaEmbeddings, ct);

        foreach (var spec in CorpusSpec.All)
        {
            if (!policies.TryGetValue(spec.PolicyNumber, out var policy))
            {
                policy = new Policy
                {
                    PolicyNumber = spec.PolicyNumber,
                    ProductLine = spec.ProductLine,
                    Name = spec.PolicyName
                };
                _db.Policies.Add(policy);
                policies[spec.PolicyNumber] = policy;
                await _db.SaveChangesAsync(ct);
            }

            PolicyVersion? supersedes = null;
            if (spec.SupersedesPolicyNumber is not null &&
                versions.TryGetValue((spec.SupersedesPolicyNumber, spec.Version - 1), out var previous))
            {
                supersedes = previous;
            }

            var version = new PolicyVersion
            {
                Policy = policy,
                Version = spec.Version,
                EffectiveDate = ToUtc(spec.EffectiveDate),
                SupersedesVersionId = supersedes?.Id
            };
            _db.PolicyVersions.Add(version);
            versions[(spec.PolicyNumber, spec.Version)] = version;
            await _db.SaveChangesAsync(ct);

            AddCoverageItems(version, spec);
            foreach (var exclusion in spec.Exclusions)
            {
                _db.Exclusions.Add(new Exclusion
                {
                    PolicyVersion = version,
                    Code = exclusion.Code,
                    Name = exclusion.Name,
                    Description = exclusion.Description
                });
            }

            foreach (var section in spec.Sections)
            {
                var chunk = new PolicyChunk
                {
                    PolicyVersion = version,
                    Content = section.Text,
                    Section = section.Title,
                    Clause = section.Clause,
                    Page = section.Page,
                    ContentHash = Hash(section.Text),
                    TokenCount = EstimateTokens(section.Text)
                };

                if (embeddingEnabled)
                {
                    try
                    {
                        var result = await ollamaEmbeddings.EmbedAsync(section.Text, ct);
                        chunk.Embedding = result.Vector;
                        chunk.Embeddings.Add(NewEmbedding(chunk, result));
                    }
                    catch (Exception ex)
                    {
                        chunk.Embedding = DeterministicEmbedding(section.Text);
                        chunk.Embeddings.Add(NewLegacyOllamaEmbedding(chunk));
                        _logger.LogDebug(ex, "Embedding failed for {Clause}; using deterministic fallback.",
                            section.Clause);
                    }
                }
                else
                {
                    chunk.Embedding = DeterministicEmbedding(section.Text);
                    chunk.Embeddings.Add(NewLegacyOllamaEmbedding(chunk));
                }

                _db.PolicyChunks.Add(chunk);
            }
        }

        await _db.SaveChangesAsync(ct);
        await SeedGeminiEmbeddingsAsync(ct);
        await SeedClaimsAsync(ct);

        _logger.LogInformation(
            "Seeded {Policies} policies, {Versions} wordings, {Coverage} coverage rows, {Exclusions} exclusions, " +
            "{Chunks} chunks.", policies.Count, versions.Count,
            await _db.CoverageItems.CountAsync(ct), await _db.Exclusions.CountAsync(ct),
            await _db.PolicyChunks.CountAsync(ct));
    }

    private void AddCoverageItems(PolicyVersion version, WordingSpec spec)
    {
        if (spec.Deductible > 0)
        {
            _db.CoverageItems.Add(new CoverageItem
            {
                PolicyVersion = version,
                Code = "DEDUCTIBLE",
                Name = "Per-Claim Deductible",
                Type = CoverageType.Deductible,
                Amount = spec.Deductible,
                Description = "Flat deductible subtracted before the coverage limit is applied."
            });
        }

        if (spec.Coinsurance is { } rate)
        {
            _db.CoverageItems.Add(new CoverageItem
            {
                PolicyVersion = version,
                Code = "COINSURANCE",
                Name = "Coinsurance - Company Share",
                Type = CoverageType.Coinsurance,
                PercentageRate = rate,
                Description = $"The company pays {rate:P0} of eligible amounts after the deductible."
            });
        }

        if (spec.CoverageLimit is { } limit)
        {
            _db.CoverageItems.Add(new CoverageItem
            {
                PolicyVersion = version,
                Code = "LIMIT",
                Name = "Coverage Limit",
                Type = CoverageType.Limit,
                Amount = limit,
                Description = $"Maximum payable per occurrence under this wording version."
            });
        }
    }

    private static PolicyChunkEmbedding NewEmbedding(PolicyChunk chunk, EmbeddingResult result) => new()
    {
        PolicyChunk = chunk, Provider = result.Provider, Model = result.Model, Vector = result.Vector
    };

    private static PolicyChunkEmbedding NewLegacyOllamaEmbedding(PolicyChunk chunk) => new()
    {
        PolicyChunk = chunk, Provider = "ollama", Model = "deterministic-local-fallback", Vector = chunk.Embedding!
    };

    private async Task SeedGeminiEmbeddingsAsync(CancellationToken ct)
    {
        try
        {
            var provider = _embeddingProviders.Get(AiPipeline.Gemini);
            var chunks = await _db.PolicyChunks.Include(x => x.Embeddings).ToListAsync(ct);
            var missing = chunks.Where(x => x.Embeddings.All(e => e.Provider != provider.ProviderName)).ToList();
            if (missing.Count == 0) return;
            var vectors = await provider.EmbedBatchAsync(missing.Select(x => x.Content).ToList(), ct);
            for (var i = 0; i < missing.Count; i++) missing[i].Embeddings.Add(NewEmbedding(missing[i], vectors[i]));
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini embeddings deferred; the complete Ollama pipeline remains available.");
        }
    }

    private async Task<bool> TryProbeEmbeddingAsync(IEmbeddingProvider provider, CancellationToken ct)
    {
        try
        {
            await provider.EmbedAsync("probe", ct);
            return true;
        }
        catch
        {
            _logger.LogWarning("Ollama embedding provider unavailable during seed; chunks will use " +
                "deterministic fallback vectors. Start Ollama and re-seed for semantic retrieval.");
            return false;
        }
    }

    private async Task SeedClaimsAsync(CancellationToken ct)
    {
        var claims = new[]
        {
            NewClaim("CLAIM-2023-001", "AUT-2022", new DateTime(2023, 3, 10), 6200m,
                "Rear-end collision during evening commute; rear bumper and trunk damaged."),
            NewClaim("CLAIM-2026-001", "AUT-2022", new DateTime(2026, 1, 20), 7800m,
                "Multi-vehicle collision on highway; front-end crumple and airbag deployment."),
            NewClaim("CLAIM-2021-014", "AUT-PREM-2019", new DateTime(2021, 9, 1), 18000m,
                "Vandalism and vehicle fire; interior and engine bay damage."),
            NewClaim("CLAIM-2022-020", "AUT-COMPACT-2020", new DateTime(2022, 2, 15), 4250m,
                "Parking lot side-swipe; door and quarter panel damage."),
            NewClaim("CLAIM-2020-007", "HLT-2019", new DateTime(2020, 5, 1), 3000m,
                "Outpatient surgical procedure and follow-up visits within calendar year limits."),
            NewClaim("CLAIM-2024-101", "HLT-2023", new DateTime(2024, 1, 15), 1400m,
                "Experimental oncology consultation billed as outpatient expense."),
            NewClaim("CLAIM-2022-033", "HOM-2018", new DateTime(2022, 6, 1), 35000m,
                "Kitchen fire damage to dwelling and contents."),
            NewClaim("CLAIM-2023-041", "HOM-2018", new DateTime(2023, 5, 1), 30000m,
                "Flood damage from storm surge; water entered dwelling from outside."),
            NewClaim("CLAIM-2024-070", "HOM-2018", new DateTime(2024, 7, 10), 12000m,
                "Flood damage with FloodGuard add-on attached; basement and finished floors."),
            NewClaim("CLAIM-2023-052", "TRV-STD-2022", new DateTime(2023, 6, 1), 2600m,
                "Trip cancelled when insured sustained injury skydiving; non-refundable tour charges."),
            NewClaim("CLAIM-2024-120", "TRV-PREM-2023", new DateTime(2024, 3, 5), 9500m,
                "Trip cancellation due to sudden illness; unused airfare and hotel."),
            NewClaim("CLAIM-2022-090", "LIF-ACCIDENT-2020", new DateTime(2022, 3, 1), 100000m,
                "Accidental death benefit claim following a boating accident.")
        };

        _db.Claims.AddRange(claims);
        await _db.SaveChangesAsync(ct);
    }

    private static Claim NewClaim(
        string number, string policyNumber, DateTime incidentDate, decimal amount, string description) => new()
    {
        ClaimNumber = number,
        PolicyNumber = policyNumber,
        IncidentDate = ToUtc(incidentDate),
        ClaimAmount = amount,
        Description = description,
        Status = ClaimStatus.Submitted
    };

    private static DateTime ToUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / 4d);

    /// <summary>
    /// Deterministic 768-dim embedding fallback so hybrid retrieval still returns
    /// stable results when Ollama is not running during seeding.
    /// </summary>
    private static float[] DeterministicEmbedding(string text)
    {
        const int dims = 768;
        var result = new float[dims];
        uint hash = 2166136261;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619;
            result[hash % dims] += 1f;
        }
        for (var i = 1; i < text.Length; i++)
        {
            hash ^= (uint)text[i - 1] * 31u;
            hash *= 16777619;
            result[(hash + (uint)i) % dims] += 0.5f;
        }

        var norm = 0f;
        foreach (var v in result) norm += v * v;
        norm = MathF.Sqrt(norm);
        if (norm == 0f) norm = 1f;
        for (var i = 0; i < result.Length; i++) result[i] /= norm;
        return result;
    }
}
