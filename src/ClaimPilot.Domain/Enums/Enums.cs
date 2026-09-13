namespace ClaimPilot.Domain.Enums;

public enum ClaimStatus
{
    Submitted = 1,
    InReview = 2,
    Approved = 3,
    Rejected = 4,
    Edited = 5,
    Closed = 6
}

public enum ApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Edited = 4,
    Escalated = 5
}

public enum ApprovalAction
{
    Approved = 1,
    Rejected = 2,
    Edited = 3,
    ReReview = 4,
    Assigned = 5,
    Escalated = 6,
    PriorityOverride = 7,
    Created = 8
}

public enum AgentType
{
    CoverageMatcher = 1,
    ExclusionAnalyst = 2,
    AdjudicationDrafter = 3,
    AnomalyDetector = 4
}

public enum DecisionType
{
    Approve = 1,
    Reject = 2,
    InsufficientInformation = 3
}

public enum AnomalySeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum Priority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

public enum UserRole
{
    Adjuster = 1,
    Supervisor = 2,
    Viewer = 3,
    Director = 4
}

public enum RunStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Degraded = 6
}

public enum DocumentStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4
}

public enum PolicyVersionStatus
{
    Draft = 1,
    Active = 2,
    Superseded = 3,
    Retired = 4
}

public enum PolicyStatus
{
    Draft = 1,
    Active = 2,
    Retired = 3
}

public enum CoverageType
{
    Limit = 1,
    Deductible = 2,
    Coinsurance = 3,
    Benefit = 4,
    Condition = 5
}

public enum AssigneeRole
{
    Adjuster = 1,
    Supervisor = 2,
    Director = 3
}