using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Domain.Services;

/// <summary>
/// Deterministic selection of the policy version applicable on the incident
/// date. A claim is always priced against the version in force on the day of
/// the incident — never the newest version. Retired versions are ineligible;
/// superseded versions remain eligible (they governed their effective window).
/// </summary>
public static class ApplicableVersionRule
{
    public static PolicyVersion? Select(IEnumerable<PolicyVersion> versions, DateTime incidentDate)
        => versions
            .Where(v => v.EffectiveDate <= incidentDate && v.Status != PolicyVersionStatus.Retired)
            .OrderByDescending(v => v.EffectiveDate)
            .FirstOrDefault();
}