namespace StatisticService.Repository.Interfaces;

// SCRUM-195: the weekly leaderboard kept in Redis, written through when a game is saved.
// Postgres is the truth; this cache can always be rebuilt from it.

// One player's totals in one week. LastEndUnixMs is the EndTime of the player's newest game that
// week: the name from that game is the one shown.
public record WeekPlayerTotals(
    int UserId,
    string UserName,
    int GamesPlayed,
    int Wins,
    int CorrectAnswers,
    int AnsweredRounds,
    long LastEndUnixMs);

// One player's result in one game, as it is added to the week.
public record CachedPlayerResult(int UserId, string UserName, bool Won, int CorrectAnswers, int AnsweredRounds);

public enum CacheWriteResult
{
    Applied,        // the game is now counted in the week
    AlreadyApplied, // the game was counted before (the same message delivered again)
    WeekNotCached,  // the week is not in Redis; the next read rebuilds it from Postgres, with this game
    Rebuilding      // the week is being rebuilt right now; try again later
}

// Redis could not be reached, or did not answer in time.
public class CacheUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface ILeaderboardCache
{
    // false when no Redis is configured: every call then does nothing.
    bool Enabled { get; }

    // Adds one game to its week. Atomic: either the whole game is counted or none of it, and a
    // gameId is only ever counted once. minGamesForRates: players below it get no winRate/accuracy entry.
    Task<CacheWriteResult> AddGameAsync(string week, Guid gameId, long endUnixMs,
        IReadOnlyList<CachedPlayerResult> players, int minGamesForRates, CancellationToken ct = default);

    // Every player who can be in a top `top` list of the week: the top `top` of each list plus
    // everybody tied with the last of them. null when the week is not in the cache.
    Task<IReadOnlyList<WeekPlayerTotals>?> GetCandidatesAsync(string week, int top, CancellationToken ct = default);

    // Takes the rebuild lock for the week. Returns a token, or null if another pod holds it.
    Task<string?> TryStartRebuildAsync(string week, CancellationToken ct = default);

    // Replaces the week with the given totals and marks it ready. Does nothing and returns false if
    // the lock expired and someone else took it.
    Task<bool> CompleteRebuildAsync(string week, string token, IReadOnlyList<WeekPlayerTotals> players,
        IReadOnlyList<Guid> gameIds, int minGamesForRates, CancellationToken ct = default);
}
