using Microsoft.EntityFrameworkCore;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Repositories;

public class WeeklyStatsRepository(AppDbContext db) : IWeeklyStatsRepository
{
    public async Task<IReadOnlyList<PlayerGameResult>> GetPlayerResultsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        // Npgsql only accepts Kind=Utc for timestamptz, so fail clearly instead of with a driver error.
        if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The interval must be given in UTC.");

        return await db.UserStats
            .AsNoTracking()
            .Where(u => u.Game.EndTime >= fromUtc && u.Game.EndTime < toUtc)
            .Select(u => new PlayerGameResult(
                u.UserId, u.UserName, u.Placement, u.CorrectAnswers, u.AnsweredRounds, u.Game.EndTime))
            .ToListAsync(ct);
    }
}
