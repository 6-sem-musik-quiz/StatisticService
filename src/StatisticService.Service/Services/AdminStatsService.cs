using StatisticService.Repository.Interfaces;
using StatisticService.Service.Interfaces;

namespace StatisticService.Service.Services;

// cache (SCRUM-195) is null in tests that only look at Postgres.
public class AdminStatsService(IAdminStatsRepository repository, ILeaderboardCache? cache = null) : IAdminStatsService
{
    // The k6 bots are named loadbot_N (gitops/loadtest, Platform ADR-0005).
    public const string LoadBotPrefix = "loadbot_";

    public async Task<int> DeleteLoadBotStatsAsync(string prefix, CancellationToken ct)
    {
        if (!string.Equals(prefix, LoadBotPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"Only the load-test prefix '{LoadBotPrefix}' can be deleted.", nameof(prefix));

        var deleted = await repository.DeleteUserStatsByUserNamePrefixAsync(LoadBotPrefix, ct);

        // SCRUM-195: the leaderboard cache only adds games, so it would keep showing the deleted bots.
        // Every week is rebuilt from Postgres on its next read. If Redis does not answer this throws,
        // and calling the endpoint again (it then deletes 0 rows) retries the reset.
        if (cache is { Enabled: true })
            await cache.InvalidateAllAsync(ct);

        return deleted;
    }
}
