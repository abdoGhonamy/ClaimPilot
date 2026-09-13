using System.Security.Claims;

namespace ClaimPilot.Application.Interfaces.Review;

public interface IAuthorityService
{
    Task<bool> CanApproveAsync(ClaimsPrincipal user, decimal amount, string action, CancellationToken ct);
    decimal GetThresholdForUser(ClaimsPrincipal user);
}