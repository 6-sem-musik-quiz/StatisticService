using StatisticService.Domain.DTOs;
using StatisticService.Domain.Entities;
using StatisticService.Repository.Interfaces;

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

public class GameResultService(IGameResultRepository repository) : IGameResultService
{
    // Column limits in GameStatsConfiguration / UserStatsConfiguration.
    public const int MaxCategoryNameLength = 60;
    public const int MaxUserNameLength = 20;

    public async Task<SaveResult> SaveAsync(GameFinishedMessage message, CancellationToken ct = default)
    {
        Validate(message);
        var saved = await repository.TryAddAsync(ToEntity(message), ct);
        return saved ? SaveResult.Saved : SaveResult.Duplicate;
    }

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
