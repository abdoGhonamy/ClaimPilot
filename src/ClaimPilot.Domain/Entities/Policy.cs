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
    public required string ContentHash { get; set; }
    public int? TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}