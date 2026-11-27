using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Cache;

// Used when no Redis is configured (ConnectionStrings:Redis is empty): the leaderboard is then
// always read from Postgres, as before SCRUM-195. Local runs and the API tests use this.
public sealed class NoLeaderboardCache : ILeaderboardCache
{
    public bool Enabled => false;

    public Task<CacheWriteResult> AddGameAsync(string week, Guid gameId, long endUnixMs,
        IReadOnlyList<CachedPlayerResult> players, int minGamesForRates, CancellationToken ct = default)
        => Task.FromResult(CacheWriteResult.WeekNotCached);

    public Task<IReadOnlyList<WeekPlayerTotals>?> GetCandidatesAsync(string week, int top, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WeekPlayerTotals>?>(null);

    public Task<string?> TryStartRebuildAsync(string week, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<bool> CompleteRebuildAsync(string week, string token, IReadOnlyList<WeekPlayerTotals> players,
        IReadOnlyList<Guid> gameIds, int minGamesForRates, CancellationToken ct = default)
        => Task.FromResult(false);
}
