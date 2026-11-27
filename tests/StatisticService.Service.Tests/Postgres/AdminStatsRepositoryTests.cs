using Microsoft.EntityFrameworkCore;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Repositories;
using StatisticService.Service.Results;

namespace StatisticService.Service.Tests.Postgres;

// SCRUM-197: the cleanup deletes the bots' UserStats rows in real PostgreSQL and keeps everything else.
public class AdminStatsRepositoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task DeleteUserStatsByUserNamePrefixAsync_DeletesOnlyLoadBots_AndKeepsTheGame()
    {
        var game = new GameFinishedMessage
        {
            GameId = Guid.NewGuid(), Rounds = 5, CatId = 7, CategoryName = "80s",
            StartTime = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 11, 27, 10, 5, 0, DateTimeKind.Utc),
            Players =
            [
                new() { UserId = 101, UserName = "loadbot_1", Score = 3, CorrectAnswers = 3, AnsweredRounds = 5, Placement = 1 },
                new() { UserId = 102, UserName = "loadbot_2", Score = 2, CorrectAnswers = 2, AnsweredRounds = 5, Placement = 2 },
                new() { UserId = 1, UserName = "alice", Score = 1, CorrectAnswers = 1, AnsweredRounds = 5, Placement = 3 },
            ],
        };
        await using (var db = postgres.NewContext())
            Assert.Equal(SaveResult.Saved, await new GameResultService(new GameResultRepository(db)).SaveAsync(game));

        int deleted;
        await using (var db = postgres.NewContext())
            deleted = await new AdminStatsRepository(db).DeleteUserStatsByUserNamePrefixAsync("loadbot_", CancellationToken.None);

        await using var check = postgres.NewContext();
        Assert.Equal(2, deleted);
        Assert.Equal(["alice"], await check.UserStats.Where(s => s.GameId == game.GameId).Select(s => s.UserName).ToListAsync());
        Assert.True(await check.GameStats.AnyAsync(g => g.Id == game.GameId));
    }
}
