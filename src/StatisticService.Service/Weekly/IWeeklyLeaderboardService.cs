using StatisticService.Domain.DTOs;

namespace StatisticService.Service.Weekly;

public interface IWeeklyLeaderboardService
{
    // week is "current" or an ISO week like "2026-W47". Throws InvalidWeekException otherwise.
    Task<WeeklyLeaderboardResponse> GetAsync(string? week, CancellationToken ct = default);
}
