using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Domain.Entities;

public class ApprovalItem
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public Guid? RunId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public Priority Priority { get; set; } = Priority.Normal;
    public DateTime? SLADeadline { get; set; }
    public string? AssignedTo { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }

    public ICollection<ApprovalHistory> History { get; set; } = new List<ApprovalHistory>();
}

public class ApprovalHistory
{
    public Guid Id { get; set; }
    public Guid ApprovalItemId { get; set; }
    public required ApprovalItem ApprovalItem { get; set; }
    public string? ReviewerId { get; set; }
    public required ApprovalAction Action { get; set; }
    public string? Comment { get; set; }
    public string? PreviousState { get; set; }
    public string? NewState { get; set; }
    public string? EditDiff { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public Guid Id { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public required string Action { get; set; }
    public string? ActorId { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string? CorrelationId { get; set; }
    public string? RunId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}