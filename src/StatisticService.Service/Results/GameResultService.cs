using StatisticService.Domain.DTOs;
using StatisticService.Domain.Entities;
using StatisticService.Repository.Interfaces;
using StatisticService.Service.Metrics;
using StatisticService.Service.Weekly;

namespace StatisticService.Service.Results;

public enum SaveResult
{
    Saved,
    Duplicate
}

// The message breaks a rule the database or the statistics rely on. Retrying will not help.
public class InvalidGameResultException(string message) : Exception(message);

public interface IGameResultService
{
    Task<SaveResult> SaveAsync(GameFinishedMessage message, CancellationToken ct = default);
}

// SCRUM-195: write-through. The game is saved in Postgres first (the truth), then added to the
// leaderboard in Redis. A CacheUnavailableException after the commit sends the message back on the
// queue; when it comes back Postgres says Duplicate, and Redis still counts it exactly once, because
// the cache remembers which gameIds it has counted. So a duplicate is also written through.
// cache/metrics are null in tests that only look at Postgres.
public class GameResultService(
    IGameResultRepository repository,
    ILeaderboardCache? cache = null,
    LeaderboardCacheMetrics? metrics = null) : IGameResultService
{
    // Column limits in GameStatsConfiguration / UserStatsConfiguration.
    public const int MaxCategoryNameLength = 60;
    public const int MaxUserNameLength = 20;

    public async Task<SaveResult> SaveAsync(GameFinishedMessage message, CancellationToken ct = default)
    {
        Validate(message);
        var game = ToEntity(message);
        var saved = await repository.TryAddAsync(game, ct);
        await AddToLeaderboardAsync(game, ct);
        return saved ? SaveResult.Saved : SaveResult.Duplicate;
    }

    private async Task AddToLeaderboardAsync(GameStats game, CancellationToken ct)
    {
        if (cache is not { Enabled: true })
            return;

        var week = WeeklyPeriod.Containing(game.EndTime).Key;
        var players = game.Players
            .Select(p => new CachedPlayerResult(p.UserId, p.UserName, p.Placement == 1, p.CorrectAnswers, p.AnsweredRounds))
            .ToList();

        CacheWriteResult result;
        try
        {
            result = await cache.AddGameAsync(week, game.Id, ToUnixMs(game.EndTime), players,
                WeeklyLeaderboardService.MinGamesForRates, ct);
        }
        catch (CacheUnavailableException)
        {
            metrics?.Write("failed");
            throw;
        }

        metrics?.Write(result switch
        {
            CacheWriteResult.Applied => "applied",
            CacheWriteResult.AlreadyApplied => "already_applied",
            CacheWriteResult.WeekNotCached => "week_not_cached",
            _ => "rebuilding"
        });

        // The week is being rebuilt from Postgres right now, and the rebuild may have read the
        // database before this game was committed. Try again once the rebuild is done.
        if (result == CacheWriteResult.Rebuilding)
            throw new CacheUnavailableException($"the leaderboard for {week} is being rebuilt");
    }

    public static long ToUnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    public static GameStats ToEntity(GameFinishedMessage m) => new()
    {
        Id = m.GameId,
        Rounds = m.Rounds,
        StartTime = AsUtc(m.StartTime),
        EndTime = AsUtc(m.EndTime),
        CatId = m.CatId,
        CategoryName = m.CategoryName,
        Players = m.Players.Select(p => new UserStats
        {
            GameId = m.GameId,
            UserId = p.UserId,
            UserName = p.UserName,
            Score = p.Score,
            CorrectAnswers = p.CorrectAnswers,
            AnsweredRounds = p.AnsweredRounds,
            Placement = p.Placement
        }).ToList()
    };

    // A whole message is rejected rather than saved with changed data (e.g. a cut-off name).
    private static void Validate(GameFinishedMessage m)
    {
        if (m.GameId == Guid.Empty) throw new InvalidGameResultException("gameId is missing");
        if (m.Rounds <= 0) throw new InvalidGameResultException("rounds must be positive");
        if (m.EndTime < m.StartTime) throw new InvalidGameResultException("endTime is before startTime");
        if (string.IsNullOrWhiteSpace(m.CategoryName) || m.CategoryName.Length > MaxCategoryNameLength)
            throw new InvalidGameResultException($"categoryName must be 1-{MaxCategoryNameLength} characters");
        if (m.Players is not { Count: > 0 }) throw new InvalidGameResultException("players is empty");

        if (m.Players.Select(p => p.UserId).Distinct().Count() != m.Players.Count)
            throw new InvalidGameResultException("a userId appears twice");

        foreach (var p in m.Players)
        {
            if (string.IsNullOrWhiteSpace(p.UserName) || p.UserName.Length > MaxUserNameLength)
                throw new InvalidGameResultException($"userName for user {p.UserId} must be 1-{MaxUserNameLength} characters");
            if (p.CorrectAnswers < 0 || p.AnsweredRounds < 0 || p.CorrectAnswers > p.AnsweredRounds)
                throw new InvalidGameResultException($"answer counts for user {p.UserId} are inconsistent");
            if (p.Placement < 1) throw new InvalidGameResultException($"placement for user {p.UserId} must be 1 or more");
        }
    }

    // GameService sends UTC with a "Z". Without one the value is taken as UTC, since
    // Postgres "timestamp with time zone" columns only accept UTC DateTimes.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
