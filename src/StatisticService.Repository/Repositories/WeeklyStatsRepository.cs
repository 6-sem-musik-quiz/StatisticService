using Microsoft.EntityFrameworkCore;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Repositories;

// SCRUM-202: the leaderboard fallback reads from the read replicas; the rebuild of the Redis
// leaderboard reads from the primary. The rebuild records which games it counted, and a game the
// cache skipped while the week was not cached is only counted by the rebuild. A replica that is
// a few milliseconds behind could miss a game committed just before, and the cache would then
// miss it for the rest of the week. The fallback is read again on the next request, so a short
// lag there does no harm.
public class WeeklyStatsRepository(AppDbContext primary, StatsReadDbContext replicas) : IWeeklyStatsRepository
{
    public async Task<IReadOnlyList<PlayerGameResult>> GetPlayerResultsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        EnsureUtc(fromUtc, toUtc);

        return await replicas.UserStats
            .AsNoTracking()
            .Where(u => u.Game.EndTime >= fromUtc && u.Game.EndTime < toUtc)
            .Select(u => new PlayerGameResult(
                u.UserId, u.UserName, u.Placement, u.CorrectAnswers, u.AnsweredRounds, u.Game.EndTime))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GamePlayerResult>> GetPlayerResultsWithGameAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        EnsureUtc(fromUtc, toUtc);

        return await primary.UserStats
            .AsNoTracking()
            .Where(u => u.Game.EndTime >= fromUtc && u.Game.EndTime < toUtc)
            .Select(u => new GamePlayerResult(u.GameId, new PlayerGameResult(
                u.UserId, u.UserName, u.Placement, u.CorrectAnswers, u.AnsweredRounds, u.Game.EndTime)))
            .ToListAsync(ct);
    }

    // Npgsql only accepts Kind=Utc for timestamptz, so fail clearly instead of with a driver error.
    private static void EnsureUtc(DateTime fromUtc, DateTime toUtc)
    {
        if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The interval must be given in UTC.");
    }
}
