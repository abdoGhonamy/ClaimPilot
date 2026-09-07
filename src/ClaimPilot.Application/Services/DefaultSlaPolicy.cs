using ClaimPilot.Application.Interfaces.Assignment;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Services;

public sealed class DefaultSlaPolicy : ISlaPolicy
{
    public DefaultSlaPolicy(SlaRuleSet rules)
    {
        Rules = rules;
    }

    public SlaRuleSet Rules { get; }

    public DateTime? ComputeDeadline(DateTime created, Priority priority)
    {
        var hours = priority switch
        {
            Priority.Critical => 4,
            Priority.High => 8,
            _ => 24
        };
        return created.AddHours(hours);
    }

    public string EvaluateEscalation(DateTime createdAt, DateTime? reviewedAt, string? assignedTo, DateTime now)
    {
        if (reviewedAt.HasValue)
            return "none";

        var unscaledCare = (now - createdAt);

        if (assignedTo is null)
            return unscaledCare > Rules.UnassignedAfter ? Rules.UnassignedAction : "none";

        if (unscaledCare > Rules.LateAfter)
            return Rules.LateAction;

        if (unscaledCare > Rules.UnreviewedAfter)
            return Rules.UnreviewedAction;

        return "none";
    }
}