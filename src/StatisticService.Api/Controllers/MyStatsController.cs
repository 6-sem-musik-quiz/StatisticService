using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatisticService.Domain.DTOs;
using StatisticService.Service.Interfaces;

namespace StatisticService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/statistics")]
public class MyStatsController(IMyStatsService service) : ControllerBase
{
    // The user id is the JWT "sub" claim; it never comes from the request.
    [HttpGet("me")]
    [ProducesResponseType<MyStatsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(sub, out var userId))
            return Unauthorized();

        return Ok(await service.GetMyStatsAsync(userId, ct));
    }
}
