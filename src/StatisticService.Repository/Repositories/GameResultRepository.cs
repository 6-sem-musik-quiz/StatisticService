using Microsoft.EntityFrameworkCore;
using Npgsql;
using StatisticService.Domain.Entities;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Repositories;

public class GameResultRepository(AppDbContext db) : IGameResultRepository
{
    public async Task<bool> TryAddAsync(GameStats game, CancellationToken ct = default)
    {
        if (await db.GameStats.AnyAsync(g => g.Id == game.Id, ct))
            return false;

        db.GameStats.Add(game);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        // Two pods got the same message at once and the other one saved it first.
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
