using StatisticService.Domain.DTOs;

namespace StatisticService.Service.Interfaces;

public interface IMyStatsService
{
    Task<MyStatsResponse> GetMyStatsAsync(int userId, CancellationToken ct = default);
}
