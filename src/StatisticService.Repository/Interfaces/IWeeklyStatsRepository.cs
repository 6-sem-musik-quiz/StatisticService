using StatisticService.Domain.DTOs;

namespace StatisticService.Repository.Interfaces;

// One player's result together with the game it is from (SCRUM-195: rebuilding the cache needs both).
public record GamePlayerResult(Guid GameId, PlayerGameResult Result);

public interface IWeeklyStatsRepository
{
    // Every player result for games with fromUtc <= EndTime < toUtc (half-open, UTC).
    Task<IReadOnlyList<PlayerGameResult>> GetPlayerResultsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    // The same rows with their gameId, in ONE query: the results and the list of counted games must
    // come from the same snapshot, or a game saved in between is marked counted without being counted.
    Task<IReadOnlyList<GamePlayerResult>> GetPlayerResultsWithGameAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
