using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Dtos;
using ClaimPilot.Application.Services;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/ask")]
[Authorize(Roles = "Adjuster,Supervisor")]
public sealed class AskController : ControllerBase
{
    private readonly AskService _ask;

    public AskController(AskService ask) => _ask = ask;

    /// <summary>
    /// Grounded Q&amp;A over the policy corpus. The service refuses to answer when
    /// the retrieved text does not support the answer rather than hallucinate.
    /// Version selection is pinned by incident date when provided.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AskResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AskResponse>> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        var result = await _ask.AskAsync(request.Question, request.PolicyNumber, request.IncidentDate, ct);

        return Ok(new AskResponse(
            result.Answer,
            result.Refused,
            result.RefusalReason,
            result.Citations.Select(c => new CitationDto(
                c.PolicyNumber, c.Citation.Version, c.Citation.Section, c.Citation.Clause, c.Citation.Page,
                c.Citation.TextExcerpt ?? string.Empty, c.Citation.Source ?? string.Empty, c.Score)).ToList()));
    }
}