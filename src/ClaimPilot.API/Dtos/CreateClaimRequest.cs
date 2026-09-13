using System.ComponentModel.DataAnnotations;

namespace ClaimPilot.API.Dtos;

public sealed record CreateClaimRequest(
    [Required, MaxLength(64)] string PolicyNumber,
    [Required] DateTime IncidentDate,
    [Required, Range(typeof(decimal), "0.01", "10000000")] decimal ClaimAmount,
    [Required, MinLength(20), MaxLength(4000)] string Description);
