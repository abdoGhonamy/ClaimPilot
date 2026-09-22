using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Domain.Entities;

public class Policy
{
    public Guid Id { get; set; }
    public required string PolicyNumber { get; set; }
    public required string ProductLine { get; set; }
    public required string Name { get; set; }
    public PolicyStatus Status { get; set; } = PolicyStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PolicyVersion> Versions { get; set; } = new List<PolicyVersion>();
    public ICollection<Claim> Claims { get; set; } = new List<Claim>();
}

public class PolicyVersion
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public required Policy Policy { get; set; }
    public required int Version { get; set; }
    public required DateTime EffectiveDate { get; set; }
    public Guid? SupersedesVersionId { get; set; }
    public PolicyVersionStatus Status { get; set; } = PolicyVersionStatus.Active;

    public ICollection<PolicyChunk> Chunks { get; set; } = new List<PolicyChunk>();
    public ICollection<CoverageItem> CoverageItems { get; set; } = new List<CoverageItem>();
    public ICollection<Exclusion> Exclusions { get; set; } = new List<Exclusion>();
}

public class PolicyChunk
{
    public Guid Id { get; set; }
    public Guid PolicyVersionId { get; set; }
    public required PolicyVersion PolicyVersion { get; set; }
    public required string Content { get; set; }
    public required string Section { get; set; }
    public required string Clause { get; set; }
    public int? Page { get; set; }
    public string? Metadata { get; set; }
    public float[]? Embedding { get; set; }
    public required string ContentHash { get; set; }
    public int? TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<PolicyChunkEmbedding> Embeddings { get; set; } = new List<PolicyChunkEmbedding>();
}

/// <summary>Provider-isolated vector representation. Never compare vectors across providers.</summary>
public class PolicyChunkEmbedding
{
    public Guid Id { get; set; }
    public Guid PolicyChunkId { get; set; }
    public required PolicyChunk PolicyChunk { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required float[] Vector { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
