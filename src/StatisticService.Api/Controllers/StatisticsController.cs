using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatisticService.Domain.DTOs;
using StatisticService.Service.Weekly;

namespace StatisticService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/statistics")]
public class StatisticsController(IWeeklyLeaderboardService weeklyLeaderboard) : ControllerBase
{
    // week = "current" or an ISO week such as "2026-W47". No week parameter at all means "current";
    // an explicit but empty or malformed value is an invalid week. InvalidWeekException is turned
    // into 400 by GlobalExceptionHandler.
    [HttpGet("weekly")]
    [ProducesResponseType<WeeklyLeaderboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<WeeklyLeaderboardResponse>> GetWeekly(CancellationToken ct = default)
    {
        // Read the raw query value: model binding would turn "?week=" into the default instead of an error.
        var week = Request.Query.TryGetValue("week", out var values) ? values.ToString() : "current";
        return Ok(await weeklyLeaderboard.GetAsync(week, ct));
    }
}
