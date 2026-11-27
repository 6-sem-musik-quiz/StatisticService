using Microsoft.Extensions.Logging;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Interfaces;
using StatisticService.Service.Metrics;
using StatisticService.Service.Results;

namespace StatisticService.Service.Weekly;

// SCRUM-195: the leaderboard is read from Redis (written through by GameResultService):
//   hit       the week is in Redis: only the possible top players are read, not the whole week.
//   miss      the week is not in Redis (never read, expired, Redis restarted): read Postgres and
//             rebuild the week in Redis from that same read, so the next request is a hit.
//   fallback  Redis does not answer: read Postgres, as before SCRUM-195.
// Both paths rank with the same Rank method, so Redis and Postgres give the same answer.
// cache/metrics/logger are null in tests that only look at Postgres.
public class WeeklyLeaderboardService(
    IWeeklyStatsRepository repository,
    TimeProvider timeProvider,
    ILeaderboardCache? cache = null,
    LeaderboardCacheMetrics? metrics = null,
    ILogger<WeeklyLeaderboardService>? logger = null) : IWeeklyLeaderboardService
{
    public const int TopCount = 10;

    // winRate and accuracy need at least this many games in the week; mostWins has no minimum.
    public const int MinGamesForRates = 5;

    public async Task<WeeklyLeaderboardResponse> GetAsync(string? week, CancellationToken ct = default)
    {
        if (!WeeklyPeriod.TryParse(week, timeProvider.GetUtcNow(), out var period))
            throw new InvalidWeekException(week);

        if (cache is not { Enabled: true })
            return Build(period, await repository.GetPlayerResultsAsync(period.StartUtc, period.EndUtc, ct));

        string? rebuildToken;
        try
        {
            var candidates = await cache.GetCandidatesAsync(period.Key, TopCount, ct);
            if (candidates is not null)
            {
                metrics?.Read("hit");
                return Rank(period, candidates);
            }

            // Take the lock BEFORE reading Postgres. A game committed after this point finds the
            // lock in Redis, is sent back on the queue, and is counted once the rebuild is done.
            rebuildToken = await cache.TryStartRebuildAsync(period.Key, ct);
            metrics?.Read("miss");
        }
        catch (CacheUnavailableException e)
        {
            logger?.LogWarning(e, "Leaderboard cache unavailable; reading week {Week} from Postgres", period.Key);
            metrics?.Read("fallback");
            return Build(period, await repository.GetPlayerResultsAsync(period.StartUtc, period.EndUtc, ct));
        }

        // Another pod is rebuilding this week: just answer from Postgres.
        if (rebuildToken is null)
            return Build(period, await repository.GetPlayerResultsAsync(period.StartUtc, period.EndUtc, ct));

        var rows = await repository.GetPlayerResultsWithGameAsync(period.StartUtc, period.EndUtc, ct);
        var results = rows.Select(r => r.Result).ToList();
        var totals = Aggregate(results);
        try
        {
            var gameIds = rows.Select(r => r.GameId).Distinct().ToList();
            if (!await cache.CompleteRebuildAsync(period.Key, rebuildToken, totals, gameIds, MinGamesForRates, ct))
                logger?.LogWarning("The rebuild of week {Week} lost its lock and was thrown away", period.Key);
        }
        catch (CacheUnavailableException e)
        {
            // The lock expires by itself; the next read tries again.
            logger?.LogWarning(e, "Could not rebuild week {Week} in the leaderboard cache", period.Key);
        }

        return Rank(period, totals);
    }

    // Pure aggregation, kept separate from I/O.
    public static WeeklyLeaderboardResponse Build(WeeklyPeriod period, IEnumerable<PlayerGameResult> results)
        => Rank(period, Aggregate(results));

    // One row per player: the week's totals. These are what Redis keeps per player.
    public static IReadOnlyList<WeekPlayerTotals> Aggregate(IEnumerable<PlayerGameResult> results)
        => results
            .GroupBy(r => r.UserId)
            .Select(g =>
            {
                // A player can be renamed during the week: show the name from the latest game.
                var newest = g.MaxBy(r => r.EndTime)!;
                return new WeekPlayerTotals(
                    UserId: g.Key,
                    UserName: newest.UserName,
                    GamesPlayed: g.Count(),
                    Wins: g.Count(r => r.Placement == 1), // a shared first place is a win for everybody with Placement 1
                    CorrectAnswers: g.Sum(r => r.CorrectAnswers),
                    AnsweredRounds: g.Sum(r => r.AnsweredRounds),
                    LastEndUnixMs: GameResultService.ToUnixMs(newest.EndTime));
            })
            .ToList();

    // The three top lists from per-player totals. `totals` may be every player of the week (Postgres)
    // or only the possible top players (Redis): the result is the same.
    public static WeeklyLeaderboardResponse Rank(WeeklyPeriod period, IEnumerable<WeekPlayerTotals> totals)
    {
        var players = totals
            .Select(t => new PlayerWeek(
                t.UserId,
                t.UserName,
                t.GamesPlayed,
                t.Wins,
                WinRate: (double)t.Wins / t.GamesPlayed,
                Accuracy: t.AnsweredRounds == 0 ? 0 : (double)t.CorrectAnswers / t.AnsweredRounds))
            .ToList();

        // Players without a win are left out of mostWins (a "most wins" list of zeros says nothing).
        var mostWins = Top(players.Where(p => p.Wins > 0), p => p.Wins)
            .Select(p => new MostWinsEntry(p.UserId, p.UserName, p.Wins, p.GamesPlayed))
            .ToList();

        var qualified = players.Where(p => p.GamesPlayed >= MinGamesForRates).ToList();
        var winRate = Top(qualified, p => p.WinRate)
            .Select(p => new WinRateEntry(p.UserId, p.UserName, p.WinRate, p.GamesPlayed))
            .ToList();
        var accuracy = Top(qualified, p => p.Accuracy)
            .Select(p => new AccuracyEntry(p.UserId, p.UserName, p.Accuracy, p.GamesPlayed))
            .ToList();

        return new WeeklyLeaderboardResponse(period.WeekStart, period.WeekEnd, mostWins, winRate, accuracy);
    }

    // Highest value first; ties: most games, then user name (ordinal, case-insensitive), then id for a stable order.
    private static IEnumerable<PlayerWeek> Top<TValue>(IEnumerable<PlayerWeek> players, Func<PlayerWeek, TValue> value)
        where TValue : IComparable<TValue>
        => players
            .OrderByDescending(value)
            .ThenByDescending(p => p.GamesPlayed)
            .ThenBy(p => p.UserName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.UserId)
            .Take(TopCount);

    private sealed record PlayerWeek(int UserId, string UserName, int GamesPlayed, int Wins, double WinRate, double Accuracy);
}
