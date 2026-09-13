using Microsoft.Extensions.Options;

using System.Security.Claims;

using ClaimPilot.Application.Interfaces.Review;

namespace ClaimPilot.Application.Services;

public sealed class AuthorityService : IAuthorityService
{
    private readonly ApprovalAuthorityOptions _options;

    public AuthorityService(IOptions<ApprovalAuthorityOptions> options) => _options = options.Value;

    public Task<bool> CanApproveAsync(ClaimsPrincipal user, decimal amount, string action, CancellationToken ct)
    {
        if (action is "Approve" or "Edit")
        {
            return Task.FromResult(amount <= GetThresholdForUser(user));
        }

        return Task.FromResult(true);
    }

    public decimal GetThresholdForUser(ClaimsPrincipal user)
    {
        if (user.IsInRole("Director")) return _options.Director;
        if (user.IsInRole("Supervisor")) return _options.Supervisor;
        if (user.IsInRole("Adjuster")) return _options.Adjuster;
        return 0m;
    }

    public decimal GetThresholdForRole(string role) => role switch
    {
        "Adjuster" => _options.Adjuster,
        "Supervisor" => _options.Supervisor,
        "Director" => _options.Director,
        _ => 0m
    };
}