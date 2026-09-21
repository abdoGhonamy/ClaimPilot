using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.Application.Interfaces;
using ClaimPilot.Application.Interfaces.Review;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/statistics")]
[Authorize(Roles = "Supervisor,Director")]
public sealed class StatisticsController : ControllerBase
{
    private readonly IReviewStatisticsService _statistics;

    public StatisticsController(IReviewStatisticsService statistics) => _statistics = statistics;

    [HttpGet]
    [ProducesResponseType<ReviewStatistics>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewStatistics>> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _statistics.GetAsync(from, to, ct));
}
