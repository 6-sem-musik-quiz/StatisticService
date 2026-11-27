namespace StatisticService.Service.Interfaces;

// SCRUM-197: cleanup after a load test.
public interface IAdminStatsService
{
    // Deletes the load-test bots' stats. Only the prefix "loadbot_" is accepted, so real players'
    // stats can never be removed this way; anything else throws ArgumentException. Returns the count.
    Task<int> DeleteLoadBotStatsAsync(string prefix, CancellationToken ct);
}
