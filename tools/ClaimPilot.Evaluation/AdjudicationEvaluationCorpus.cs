namespace ClaimPilot.Evaluation;

/// <summary>
/// Ground-truth cases for the three adjudication specialists. Each case is run
/// through POST /api/claims/{claimId}/adjudicate and evaluated from its SSE events.
/// </summary>
public sealed record AdjudicationEvalCase(
    string Id,
    string Name,
    string PolicyNumber,
    DateTime IncidentDate,
    decimal ClaimAmount,
    string Description,
    int ExpectedPolicyVersion,
    string? ExpectedExclusionCode = null,
    string? ExpectedAnomalyType = null);

public static class AdjudicationEvaluationCorpus
{
    public static IReadOnlyList<AdjudicationEvalCase> All { get; } = new List<AdjudicationEvalCase>
    {
        new(
            "ADJ-COVERAGE-01", "Coverage Matcher selects the historical wording",
            "AUT-2022", new DateTime(2023, 3, 10), 1_400m,
            "Rear-end collision damaged the rear bumper and trunk during the evening commute.",
            ExpectedPolicyVersion: 1),
        new(
            "ADJ-EXCLUSION-01", "Exclusion Analyst identifies mechanical breakdown",
            "AUT-2022", new DateTime(2023, 3, 10), 1_400m,
            "The engine stopped because of a mechanical breakdown and needs an internal repair.",
            ExpectedPolicyVersion: 1, ExpectedExclusionCode: "EX-9"),
        new(
            "ADJ-ANOMALY-01", "Anomaly Detector flags a high claim-to-limit ratio",
            "AUT-2022", new DateTime(2023, 3, 10), 4_500m,
            "Rear-end collision damaged the bumper, trunk, and rear safety sensors during the commute.",
            ExpectedPolicyVersion: 1, ExpectedAnomalyType: "high_claim_ratio")
    };
}
