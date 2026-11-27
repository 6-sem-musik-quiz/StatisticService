using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StatisticService.Service.Interfaces;

namespace StatisticService.Api.Controllers;

// SCRUM-197 (Platform ADR-0005): cleanup after a load test. Edge: DELETE /api/statisticservice/admin/stats.
[ApiController]
[Route("api/admin/stats")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class AdminStatsController(IAdminStatsService service) : ControllerBase
{
    public record DeleteResult(int Deleted);

    [HttpDelete]
    [ProducesResponseType<DeleteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteLoadBots([FromQuery] string? prefix, CancellationToken ct)
    {
        try
        {
            return Ok(new DeleteResult(await service.DeleteLoadBotStatsAsync(prefix ?? string.Empty, ct)));
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid prefix", detail: ex.Message);
        }
    }
}

public static class AuthPolicies
{
    // The JWT claim "role" (set by UserService's TokenService) must be "Admin". Same policy as MusicService.
    public const string AdminOnly = "AdminOnly";
}
