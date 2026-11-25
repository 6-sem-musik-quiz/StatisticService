using StatisticService.Domain.DTOs;

namespace StatisticService.Repository.Interfaces;

public interface IWeeklyStatsRepository
{
    // Every player result for games with fromUtc <= EndTime < toUtc (half-open, UTC).
    Task<IReadOnlyList<PlayerGameResult>> GetPlayerResultsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
