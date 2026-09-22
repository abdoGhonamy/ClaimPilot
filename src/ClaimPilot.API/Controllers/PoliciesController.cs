using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Dtos;
using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Repositories;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/policies")]
[Authorize(Roles = "Adjuster,Supervisor,Director,Viewer")]
public sealed class PoliciesController : ControllerBase
{
    private readonly IPolicyRepository _policies;
    private readonly IAuditService _audit;

    public PoliciesController(IPolicyRepository policies, IAuditService audit)
    {
        _policies = policies;
        _audit = audit;
    }

    /// <summary>Returns active policies for policy selection controls.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PolicyOptionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PolicyOptionDto>>> List(CancellationToken ct)
    {
        var policies = await _policies.GetActiveAsync(ct);
        return Ok(policies.Select(p => new PolicyOptionDto(p.PolicyNumber, p.Name, p.ProductLine)).ToList());
    }

    /// <summary>Returns the complete policy wordings, coverage terms, exclusions and source sections.</summary>
    [HttpGet("{policyNumber}")]
    [ProducesResponseType<PolicyDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyDetailDto>> Get(string policyNumber, CancellationToken ct)
    {
        var policy = await _policies.GetByPolicyNumberAsync(policyNumber, ct);
        if (policy is null)
            return NotFound();

        var versions = await _policies.GetVersionsAsync(policy.Id, ct);
        var wordings = new List<PolicyWordingDto>();
        foreach (var version in versions.OrderByDescending(version => version.EffectiveDate))
        {
            var coverage = await _policies.GetCoverageItemsAsync(version.Id, ct);
            var exclusions = await _policies.GetExclusionsAsync(version.Id, ct);
            var sections = await _policies.GetChunksAsync(version.Id, ct);
            wordings.Add(new PolicyWordingDto(
                version.Version, version.EffectiveDate, version.Status.ToString(),
                coverage.Select(item => new CoverageItemDto(item.Code, item.Name, item.Type.ToString(), item.Amount,
                    item.PercentageRate, item.Description)).ToList(),
                exclusions.Select(item => new PolicyExclusionDto(item.Code, item.Name, item.Description)).ToList(),
                sections.Select(item => new PolicySectionDto(item.Section, item.Clause, item.Page, item.Content)).ToList()));
        }

        return Ok(new PolicyDetailDto(policy.PolicyNumber, policy.Name, policy.ProductLine,
            policy.Status.ToString(), wordings));
    }

    /// <summary>
    /// Seeds structured coverage terms and exclusions for a specific policy wording version.
    /// Items whose code already exists for the version are skipped without error.
    /// </summary>
    [HttpPost("{policyNumber}/versions/{version}/structured-data")]
    [Authorize(Roles = "Adjuster,Supervisor,Director")]
    [ProducesResponseType<SeedStructuredDataResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeedStructuredDataResponse>> SeedStructuredData(
        string policyNumber, int version, [FromBody] SeedStructuredDataRequest request, CancellationToken ct)
    {
        var policy = await _policies.GetByPolicyNumberAsync(policyNumber, ct);
        if (policy is null)
            return NotFound();

        var policyVersion = await _policies.GetVersionAsync(policy.Id, version, ct);
        if (policyVersion is null)
            return NotFound();

        var coverage = (request.CoverageItems ?? [])
            .Select(item => new CoverageItem
            {
                PolicyVersion = policyVersion,
                Code = item.Code.Trim(),
                Name = item.Name.Trim(),
                Type = ParseCoverageType(item.Type),
                Amount = item.Amount,
                PercentageRate = item.PercentageRate,
                Description = item.Description?.Trim()
            })
            .ToList();

        var exclusions = (request.Exclusions ?? [])
            .Select(item => new Exclusion
            {
                PolicyVersion = policyVersion,
                Code = item.Code.Trim(),
                Name = item.Name.Trim(),
                Description = item.Description.Trim()
            })
            .ToList();

        var result = await _policies.SeedStructuredDataAsync(policyVersion.Id, coverage, exclusions, ct);

        await _audit.RecordAsync(new AuditLogEntry(
            "PolicyVersion",
            policyVersion.Id,
            "StructuredDataSeeded",
            User.Identity?.Name ?? "system",
            After: JsonSerializer.Serialize(new
            {
                policy.PolicyNumber,
                Version = version,
                result.CoverageItemsInserted,
                result.CoverageItemsSkipped,
                result.ExclusionsInserted,
                result.ExclusionsSkipped
            })), ct);

        return Ok(new SeedStructuredDataResponse(
            result.CoverageItemsInserted, result.CoverageItemsSkipped,
            result.ExclusionsInserted, result.ExclusionsSkipped));
    }

    private static CoverageType ParseCoverageType(string value)
    {
        if (Enum.TryParse<CoverageType>(value, ignoreCase: true, out var type))
            return type;

        throw new ValidationException(
            $"Unknown coverage type '{value}'. Expected one of: Limit, Deductible, Coinsurance, Benefit, Condition.");
    }
}
