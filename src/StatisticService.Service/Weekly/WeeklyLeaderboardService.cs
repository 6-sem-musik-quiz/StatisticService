using StatisticService.Domain.DTOs;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Service.Weekly;

public class WeeklyLeaderboardService(IWeeklyStatsRepository repository, TimeProvider timeProvider) : IWeeklyLeaderboardService
{
    public const int TopCount = 10;

    // winRate and accuracy need at least this many games in the week; mostWins has no minimum.
    public const int MinGamesForRates = 5;

    public async Task<WeeklyLeaderboardResponse> GetAsync(string? week, CancellationToken ct = default)
    {
        if (!WeeklyPeriod.TryParse(week, timeProvider.GetUtcNow(), out var period))
            throw new InvalidWeekException(week);

        var results = await repository.GetPlayerResultsAsync(period.StartUtc, period.EndUtc, ct);
        return Build(period, results);
    }

    // Pure aggregation, kept separate from I/O.
    public static WeeklyLeaderboardResponse Build(WeeklyPeriod period, IEnumerable<PlayerGameResult> results)
    {
        var players = results
            .GroupBy(r => r.UserId)
            .Select(g =>
            {
                var games = g.Count();
                var wins = g.Count(r => r.Placement == 1); // a shared first place is a win for everybody with Placement 1
                var answered = g.Sum(r => r.AnsweredRounds);
                return new PlayerWeek(
                    UserId: g.Key,
                    // A player can be renamed during the week: show the name from the latest game.
                    UserName: g.MaxBy(r => r.EndTime)!.UserName,
                    GamesPlayed: games,
                    Wins: wins,
                    WinRate: (double)wins / games,
                    Accuracy: answered == 0 ? 0 : (double)g.Sum(r => r.CorrectAnswers) / answered);
            })
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
