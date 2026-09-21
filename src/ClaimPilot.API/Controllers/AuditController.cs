using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Dtos;
using ClaimPilot.Application.Interfaces;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Supervisor,Director")]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditService _audit;

    public AuditController(IAuditService audit) => _audit = audit;

    /// <summary>Append-only audit trail. Supports filtering by entity/run.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AuditDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuditDto>>> Query(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] string? runId,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var entries = await _audit.QueryAsync(entityType, entityId, runId, take, ct);
        return Ok(entries.Select(a => new AuditDto(a.Id, a.EntityType, a.EntityId, a.Action, a.ActorId,
            a.Before, a.After, a.CorrelationId, a.RunId, a.CreatedAt)).ToList());
    }
}
