using Microsoft.EntityFrameworkCore;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Repositories;

public class MyStatsRepository(AppDbContext db) : IMyStatsRepository
{
    public async Task<IReadOnlyList<MyGameRow>> GetMyGamesAsync(int userId, CancellationToken ct = default) =>
        await db.UserStats.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new MyGameRow(
                u.GameId, u.Game.Rounds, u.Game.StartTime, u.Game.EndTime,
                u.Game.CatId, u.Game.CategoryName,
                u.CorrectAnswers, u.AnsweredRounds, u.Placement))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CoPlayerRow>> GetCoPlayersAsync(int userId, CancellationToken ct = default) =>
        await db.UserStats.AsNoTracking()
            .Where(o => o.UserId != userId
                && db.UserStats.Any(m => m.GameId == o.GameId && m.UserId == userId))
            .Select(o => new CoPlayerRow(o.GameId, o.UserId, o.UserName))
            .ToListAsync(ct);
}
