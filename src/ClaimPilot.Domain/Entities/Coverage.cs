using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Domain.Entities;

public class CoverageItem
{
    public Guid Id { get; set; }
    public Guid PolicyVersionId { get; set; }
    public required PolicyVersion PolicyVersion { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public CoverageType Type { get; set; }
    public decimal? Amount { get; set; }
    public decimal? PercentageRate { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Exclusion
{
    public Guid Id { get; set; }
    public Guid PolicyVersionId { get; set; }
    public required PolicyVersion PolicyVersion { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}