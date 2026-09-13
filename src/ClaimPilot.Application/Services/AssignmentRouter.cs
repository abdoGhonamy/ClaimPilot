using ClaimPilot.Application.Interfaces.Review;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Services;

public static class AssignmentRouter
{
    public static AssigneeRole Compute(Priority priority, decimal amount, IAuthorityService authority)
    {
        var tier = priority switch
        {
            Priority.Critical => 3,
            Priority.High => 2,
            _ => 1
        };

        if (amount > authority.GetThresholdForRole("Supervisor")) tier = Math.Max(tier, 3);
        else if (amount > authority.GetThresholdForRole("Adjuster")) tier = Math.Max(tier, 2);

        return tier switch
        {
            3 => AssigneeRole.Director,
            2 => AssigneeRole.Supervisor,
            _ => AssigneeRole.Adjuster
        };
    }
}