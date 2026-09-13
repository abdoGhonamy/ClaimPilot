namespace ClaimPilot.Application.Services;

public sealed class ApprovalAuthorityOptions
{
    public const string SectionName = "ApprovalAuthority";

    public decimal Adjuster { get; set; } = 10_000m;
    public decimal Supervisor { get; set; } = 100_000m;
    public decimal Director { get; set; } = 1_000_000_000m;
}