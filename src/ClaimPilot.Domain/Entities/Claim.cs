using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Domain.Entities;

public class Claim
{
    public Guid Id { get; set; }
    public required string ClaimNumber { get; set; }
    public required string PolicyNumber { get; set; }
    public required DateTime IncidentDate { get; set; }
    public decimal ClaimAmount { get; set; }
    public required string Description { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ClaimDocument> Documents { get; set; } = new List<ClaimDocument>();
    public ICollection<AdjudicationRun> Runs { get; set; } = new List<AdjudicationRun>();
}

public class ClaimDocument
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public required Claim Claim { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime StoredAt { get; set; } = DateTime.UtcNow;
}