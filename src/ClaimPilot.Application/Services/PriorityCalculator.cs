using ClaimPilot.Application.Interfaces;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Application.Services;

/// <summary>
/// Priority determined from claim severity, amount and SLA window.
/// This is a pure, deterministic calculation — no LLM involvement.
/// </summary>
public sealed class PriorityCalculator : IPriorityCalculator
{
    public Priority Calculate(PriorityInput input)
    {
        var amountRatio = input.PolicyLimit.HasValue && input.PolicyLimit > 0
            ? input.ClaimAmount / input.PolicyLimit.Value
            : (input.ClaimAmount > 100000 ? 1.5m : 0.5m);

        var score = 0;

        if (amountRatio >= 1.6m) score += 3;
        else if (amountRatio >= 1.2m) score += 2;
        else if (amountRatio >= 0.8m) score += 1;

        if (input.DaysInSystem <= 2) score += 1;
        else if (input.DaysInSystem > 10) score -= 1;

        if (input.IncidentDate > DateTime.UtcNow.AddDays(-7)) score += 1;

        return score switch
        {
            >= 5 => Priority.Critical,
            >= 3 => Priority.High,
            >= 1 => Priority.Normal,
            _ => Priority.Low
        };
    }
}