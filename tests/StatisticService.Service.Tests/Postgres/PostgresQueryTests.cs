using Microsoft.EntityFrameworkCore;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Data;
using StatisticService.Repository.Repositories;
using StatisticService.Service.Results;
using StatisticService.Service.Services;
using StatisticService.Service.Weekly;
using Testcontainers.PostgreSql;

namespace StatisticService.Service.Tests.Postgres;

// SCRUM-189: the EF queries run against a real PostgreSQL (Testcontainers, Docker), with the migrations
// from SCRUM-105. The other tests replace the repositories with fakes, so these are the only ones that
// check the SQL Npgsql makes, the UTC week bounds and the write path from SCRUM-85.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync(); // the real migrations, not EnsureCreated
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}

public class PostgresQueryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const int Alice = 1, Bob = 2, Carl = 3, Dan = 4;

    // ISO week 47 of 2026 is Monday 16 November to Sunday 22 November. Copenhagen is UTC+1 then, so
    // Sunday 23:59 local is 22:59 UTC and Monday 00:00 local (week 48) is 23:00 UTC on Sunday.
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateTime Utc(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static GameFinishedPlayer P(int id, string name, int correct, int answered, int placement) =>
        new() { UserId = id, UserName = name, Score = correct, CorrectAnswers = correct, AnsweredRounds = answered, Placement = placement };

    private static GameFinishedMessage Game(DateTime start, DateTime end, int rounds, int catId, string cat, params GameFinishedPlayer[] players) =>
        new() { GameId = Guid.NewGuid(), Rounds = rounds, StartTime = start, EndTime = end, CatId = catId, CategoryName = cat, Players = [.. players] };

    // G1-G3, G5, G6 in week 47; G4 in week 48, one minute after G3's end.
    private static readonly GameFinishedMessage G1 = Game(Utc(16, 18, 0), Utc(16, 18, 8), 10, 7, "80s",
        P(Alice, "alice", 8, 10, 1), P(Bob, "bob", 6, 10, 2));
    private static readonly GameFinishedMessage G2 = Game(Utc(18, 19, 0), Utc(18, 19, 5), 5, 7, "80s",
        P(Alice, "alice", 4, 5, 1), P(Bob, "bob", 4, 5, 1), P(Carl, "carl", 2, 4, 3)); // shared first place
    private static readonly GameFinishedMessage G3 = Game(Utc(22, 22, 50), Utc(22, 22, 59), 10, 9, "90s",
        P(Bob, "bob", 9, 10, 1), P(Alice, "alice", 7, 9, 2));                         // Sunday 23:59 local
    private static readonly GameFinishedMessage G4 = Game(Utc(22, 22, 55), Utc(22, 23, 0), 5, 9, "90s",
        P(Carl, "carl", 5, 5, 1), P(Alice, "alice", 3, 5, 2));                         // Monday 00:00 local
    private static readonly GameFinishedMessage G5 = Game(Utc(19, 20, 0), Utc(19, 20, 4), 5, 7, "80s",
        P(Bob, "bob", 5, 5, 1), P(Dan, "dan", 3, 5, 2));
    private static readonly GameFinishedMessage G6 = Game(Utc(20, 20, 0), Utc(20, 20, 6), 5, 7, "80s",
        P(Dan, "dan", 5, 5, 1), P(Bob, "bob", 2, 5, 2));

    public async Task InitializeAsync()
    {
        await using var db = postgres.NewContext();
        await db.UserStats.ExecuteDeleteAsync();
        await db.GameStats.ExecuteDeleteAsync();
        foreach (var game in new[] { G1, G2, G3, G4, G5, G6 })
            Assert.Equal(SaveResult.Saved, await Save(game));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Through the real write path (SCRUM-85): GameResultService -> GameResultRepository.
    private async Task<SaveResult> Save(GameFinishedMessage game)
    {
        await using var db = postgres.NewContext();
        return await new GameResultService(new GameResultRepository(db)).SaveAsync(game);
    }

    private static WeeklyPeriod Week(string week)
    {
        Assert.True(WeeklyPeriod.TryParse(week, Now, out var period));
        return period;
    }

    // ---- WeeklyStatsRepository ----

    [Fact]
    public async Task WeeklyResults_OnlyHaveGamesThatEndedInTheWeek_InDanishTime()
    {
        await using var db = postgres.NewContext();
        var repository = new WeeklyStatsRepository(db);

        var w47 = Week("2026-W47");
        var rows47 = await repository.GetPlayerResultsAsync(w47.StartUtc, w47.EndUtc);
        var w48 = Week("2026-W48");
        var rows48 = await repository.GetPlayerResultsAsync(w48.StartUtc, w48.EndUtc);

        Assert.Equal(11, rows47.Count); // G1 2 + G2 3 + G3 2 + G5 2 + G6 2
        Assert.Contains(rows47, r => r.EndTime == G3.EndTime);        // Sunday 23:59 local: still week 47
        Assert.DoesNotContain(rows47, r => r.EndTime == G4.EndTime);  // Monday 00:00 local: week 48
        Assert.Equal([Alice, Carl], rows48.Select(r => r.UserId).Order());
        Assert.All(rows47, r => Assert.Equal(DateTimeKind.Utc, r.EndTime.Kind));
    }

    [Fact]
    public async Task WeeklyLeaderboard_FromPostgres_CountsSharedFirstPlaceAsWins_AndRatesOnlyFrom5Games()
    {
        await using var db = postgres.NewContext();
        var service = new WeeklyLeaderboardService(new WeeklyStatsRepository(db), new FixedTime(Now));

        var board = await service.GetAsync("2026-W47");

        // bob: G2 (shared), G3, G5; alice: G1, G2 (shared); dan: G6. carl has no win in week 47.
        Assert.Equal([("bob", 3, 5), ("alice", 2, 3), ("dan", 1, 2)], board.MostWins.Select(w => (w.UserName, w.Wins, w.GamesPlayed)));
        // Only bob has 5 games; alice (3), carl (1) and dan (2) are below MinGamesForRates.
        var rate = Assert.Single(board.WinRate);
        Assert.Equal(("bob", 0.6), (rate.UserName, rate.WinRate));
        var accuracy = Assert.Single(board.Accuracy);
        Assert.Equal(26.0 / 35, accuracy.Accuracy, 6); // (6+4+9+5+2) / (10+5+10+5+5)
    }

    // ---- MyStatsRepository ----

    [Fact]
    public async Task MyStats_FromPostgres_MatchEveryField()
    {
        await using var db = postgres.NewContext();
        var service = new MyStatsService(new MyStatsRepository(db));

        var me = await service.GetMyStatsAsync(Alice);

        Assert.Equal(4, me.GamesPlayed);                  // G1-G4, across both weeks
        Assert.Equal(2, me.Wins);                         // G1, and the shared first place in G2
        Assert.Equal(0.5, me.WinRate);
        Assert.Equal(22.0 / 29, me.Accuracy, 6);          // (8+4+7+3) / (10+5+9+5)
        Assert.Equal(30, me.RoundsPlayed);                // 10+5+10+5
        Assert.Equal(1620, me.TimePlayedSeconds);         // 8+5+9+5 minutes
        // 80s and 90s have two games each: the newest game (G4, 90s) decides.
        Assert.Equal(new MostPlayedCategoryDto(9, "90s", 2), me.MostPlayedCategory);
        // bob: G1, G2, G3; carl: G2, G4.
        Assert.Equal(new MostCommonFriendDto(Bob, "bob", 3), me.MostCommonFriend);
    }

    [Fact]
    public async Task MyStats_ForAPlayerWithoutGames_AreEmpty()
    {
        await using var db = postgres.NewContext();

        var nobody = await new MyStatsService(new MyStatsRepository(db)).GetMyStatsAsync(99);

        Assert.Equal(new MyStatsResponse(0, 0, 0, 0, 0, 0, null, null), nobody);
    }

    // ---- GameResultRepository (SCRUM-85, and SCRUM-105's acceptance criterion) ----

    [Fact]
    public async Task SavingTheSameGameAgain_IsADuplicate_AndAddsNoRows()
    {
        Assert.Equal(SaveResult.Duplicate, await Save(G2));

        await using var db = postgres.NewContext();
        Assert.Equal(6, await db.GameStats.CountAsync());
        Assert.Equal(3, await db.UserStats.CountAsync(u => u.GameId == G2.GameId));
    }
}
