using ClaimPilot.Application.Interfaces.Adjudication;
using ClaimPilot.Domain.ValueObjects;

namespace ClaimPilot.Application.Services;

/// <summary>
/// Pure deterministic adjudication engine. No I/O, no LLM, no randomness.
/// The same inputs always produce the same amounts and the same step trace.
/// </summary>
public sealed class DeterministicAdjudicationEngine : IAdjudicationEngine
{
    public Computation Compute(ComputationRequest request)
    {
        var steps = new List<ComputationStep>();
        decimal remaining = request.ClaimAmount;

        steps.Add(new ComputationStep
        {
            Step = "claim_amount",
            Description = "Claimed amount",
            Amount = request.ClaimAmount,
            Detail = $"Claim amount is {request.ClaimAmount:C}"
        });

        if (request.ApplicableExclusions.Count > 0)
        {
            steps.Add(new ComputationStep
            {
                Step = "exclusion",
                Description = "Applicable exclusion found",
                Amount = 0,
                Detail = $"Exclusion applies: {string.Join(", ", request.ApplicableExclusions)}"
            });
            return new Computation
            {
                ClaimAmount = request.ClaimAmount,
                Deductible = 0,
                CoinsuranceRate = null,
                CoverageLimit = null,
                CoinsuranceAmount = 0,
                CoveredAmount = 0,
                Cap = 0,
                Payable = 0,
                StepTrace = steps,
                Sufficient = true,
                InsufficiencyReason = $"Claim excluded by policy exclusion(s): {string.Join(", ", request.ApplicableExclusions)}"
            };
        }

        if (request.HasApplicableExclusions is false && request.ClaimAmount <= 0)
        {
            return new Computation
            {
                ClaimAmount = 0,
                Payable = 0,
                StepTrace = steps,
                Sufficient = false,
                InsufficiencyReason = "No claim amount provided."
            };
        }

        if (request.Deductible is { } deductible)
        {
            remaining -= deductible;
            steps.Add(new ComputationStep
            {
                Step = "deductible",
                Description = "Apply deductible",
                Amount = deductible,
                Detail = $"Deductible {deductible:C} subtracted from {request.ClaimAmount:C}; remaining {remaining:C}"
            });
        }

        if (request.CoinsuranceRate is { } rate)
        {
            decimal covered = remaining * rate;
            steps.Add(new ComputationStep
            {
                Step = "coinsurance",
                Description = "Apply coinsurance",
                Amount = covered,
                Detail = $"Coinsurance {rate:P0} applied to {remaining:C} = {covered:C}"
            });
            remaining = covered;
        }

        if (request.CoverageLimit is { } cap)
        {
            decimal capped = Math.Min(remaining, cap);
            steps.Add(new ComputationStep
            {
                Step = "coverage_limit",
                Description = "Apply coverage limit",
                Amount = capped,
                Detail = $"Limit {cap:C} applied to {remaining:C}; payable {capped:C}"
            });
            remaining = capped;
        }

        decimal payable = Math.Max(remaining, 0);
        decimal postDeductible = request.Deductible is { } d0 ? Math.Max(request.ClaimAmount - d0, 0) : request.ClaimAmount;
        decimal coinsuranceAmountExact = request.CoinsuranceRate is { } rate0 ? postDeductible * rate0 : postDeductible;

        return new Computation
        {
            ClaimAmount = request.ClaimAmount,
            Deductible = request.Deductible,
            CoinsuranceRate = request.CoinsuranceRate,
            CoverageLimit = request.CoverageLimit,
            CoinsuranceAmount = coinsuranceAmountExact,
            CoveredAmount = coinsuranceAmountExact,
            Cap = request.CoverageLimit,
            Payable = payable,
            StepTrace = steps,
            Sufficient = true
        };
    }
}