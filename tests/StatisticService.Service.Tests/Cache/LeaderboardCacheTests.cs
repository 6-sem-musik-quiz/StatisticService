using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Cache;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;
using StatisticService.Repository.Repositories;
using StatisticService.Service.Metrics;
using StatisticService.Service.Results;
using StatisticService.Service.Services;
using StatisticService.Service.Weekly;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace StatisticService.Service.Tests.Cache;

// SCRUM-195: the write-through leaderboard cache against a real Postgres and a real Redis
// (Testcontainers, Docker). The answer from Redis must always equal the answer from Postgres.
public sealed class CacheFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7.4-alpine").Build();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString() + ",allowAdmin=true");
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task FlushRedisAsync()
    {
        foreach (var server in Redis.GetServers())
            await server.FlushAllDatabasesAsync();
    }
}

public class LeaderboardCacheTests(CacheFixture fx) : IClassFixture<CacheFixture>, IAsyncLifetime
{
    private const string Week = "2026-W47";
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly LeaderboardCacheMetrics _metrics = new();
    private ILeaderboardCache Cache => new RedisLeaderboardCache(fx.Redis);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public async Task InitializeAsync()
    {
        await using var db = fx.NewContext();
        await db.UserStats.ExecuteDeleteAsync();
        await db.GameStats.ExecuteDeleteAsync();
        await fx.FlushRedisAsync();
    }

    public Task DisposeAsync()
    {
        _metrics.Dispose();
        return Task.CompletedTask;
    }

    // ---- helpers ----

    private static DateTime Utc(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static GameFinishedPlayer P(int id, string name, int correct, int answered, int placement) =>
        new() { UserId = id, UserName = name, Score = correct, CorrectAnswers = correct, AnsweredRounds = answered, Placement = placement };

    private static GameFinishedMessage Game(DateTime end, params GameFinishedPlayer[] players) =>
        new() { GameId = Guid.NewGuid(), Rounds = 5, StartTime = end.AddMinutes(-5), EndTime = end, CatId = 7, CategoryName = "80s", Players = [.. players] };

    private async Task<SaveResult> Save(GameFinishedMessage game, ILeaderboardCache cache)
    {
        await using var db = fx.NewContext();
        return await new GameResultService(new GameResultRepository(db), cache, _metrics).SaveAsync(game);
    }

    private async Task<WeeklyLeaderboardResponse> Board(ILeaderboardCache? cache)
    {
        await using var db = fx.NewContext();
        return await new WeeklyLeaderboardService(new WeeklyStatsRepository(db), new FixedTime(Now), cache, _metrics).GetAsync(Week);
    }

    // Records compare their lists by reference, so compare the JSON the API would send.
    private async Task AssertSameAsPostgres(WeeklyLeaderboardResponse fromCache)
    {
        var fromPostgres = await Board(cache: null);
        Assert.Equal(JsonSerializer.Serialize(fromPostgres), JsonSerializer.Serialize(fromCache));
    }

    // A player's totals as Redis keeps them (read directly: a player without a win and with fewer
    // than 5 games is in no top list, so GetCandidatesAsync would not return them).
    private async Task<WeekPlayerTotals> Totals(int userId)
    {
        var value = await fx.Redis.GetDatabase().HashGetAsync(RedisLeaderboardCache.Key(Week, "players"), userId);
        Assert.True(value.HasValue, $"user {userId} is not in Redis");
        return RedisLeaderboardCache.Parse(userId, value!);
    }

    private Task<long> CountedGames() => fx.Redis.GetDatabase().SetLengthAsync(RedisLeaderboardCache.Key(Week, "games"));

    // An empty week read once is in Redis ("ready"), so later games are written through to it.
    private async Task WarmWeek()
    {
        await Board(Cache);
        Assert.True(await fx.Redis.GetDatabase().KeyExistsAsync(RedisLeaderboardCache.Key(Week, "ready")));
    }

    // ---- write-through ----

    [Fact]
    public async Task AGame_IsSavedInPostgres_AndCountedInRedis()
    {
        await WarmWeek();
        var game = Game(Utc(18, 19, 0), P(1, "alice", 4, 5, 1), P(2, "bob", 3, 5, 2));

        Assert.Equal(SaveResult.Saved, await Save(game, Cache));

        await using (var db = fx.NewContext())
            Assert.Equal(2, await db.UserStats.CountAsync(u => u.GameId == game.GameId));
        Assert.Equal(new WeekPlayerTotals(1, "alice", 1, 1, 4, 5, GameResultService.ToUnixMs(game.EndTime)), await Totals(1));
        Assert.Equal(new WeekPlayerTotals(2, "bob", 1, 0, 3, 5, GameResultService.ToUnixMs(game.EndTime)), await Totals(2));

        var board = await Board(Cache);
        Assert.Equal(("alice", 1), (board.MostWins.Single().UserName, board.MostWins.Single().Wins));
        await AssertSameAsPostgres(board);
    }

    [Fact]
    public async Task TheSameMessageTwice_IsCountedOnce_InPostgresAndRedis()
    {
        await WarmWeek();
        var game = Game(Utc(18, 19, 0), P(1, "alice", 4, 5, 1), P(2, "bob", 3, 5, 2));

        Assert.Equal(SaveResult.Saved, await Save(game, Cache));
        Assert.Equal(SaveResult.Duplicate, await Save(game, Cache));

        await using (var db = fx.NewContext())
            Assert.Equal(1, await db.GameStats.CountAsync());
        Assert.Equal(1, (await Totals(1)).GamesPlayed);
        Assert.Equal(1, (await Totals(1)).Wins);
    }

    [Fact]
    public async Task AWeekNotInRedis_IsNotWrittenTo_ButRebuiltWithTheGameOnTheNextRead()
    {
        var game = Game(Utc(18, 19, 0), P(1, "alice", 4, 5, 1));

        Assert.Equal(SaveResult.Saved, await Save(game, Cache));
        Assert.False(await fx.Redis.GetDatabase().KeyExistsAsync(RedisLeaderboardCache.Key(Week, "players")));

        await AssertSameAsPostgres(await Board(Cache)); // miss: from Postgres, and rebuilt
        Assert.Equal(1, (await Totals(1)).GamesPlayed);
    }

    // ---- rebuild ----

    [Fact]
    public async Task AfterRedisIsEmptied_TheWeekIsRebuiltFromPostgres_AndNoGameIsCountedTwice()
    {
        await WarmWeek();
        var games = new[]
        {
            Game(Utc(16, 18, 0), P(1, "alice", 8, 10, 1), P(2, "bob", 6, 10, 2)),
            Game(Utc(18, 19, 0), P(1, "alice", 4, 5, 1), P(2, "bob", 4, 5, 1), P(3, "carl", 2, 4, 3)),
            Game(Utc(22, 22, 59), P(2, "bobby", 9, 10, 1), P(1, "alice", 7, 9, 2)) // Sunday 23:59 Danish time
        };
        foreach (var game in games)
            await Save(game, Cache);
        var before = await Board(Cache);

        await fx.FlushRedisAsync(); // what a Redis restart does: it has no disk

        var rebuilt = await Board(Cache);  // miss: answered from Postgres, and the week is rebuilt
        var cached = await Board(Cache);   // hit
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(rebuilt));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(cached));
        await AssertSameAsPostgres(cached);
        Assert.Equal("bobby", (await Totals(2)).UserName); // the newest game's name

        // The rebuild also remembers the counted games: a late redelivery does not count again.
        Assert.Equal(SaveResult.Duplicate, await Save(games[0], Cache));
        Assert.Equal(3, (await Totals(1)).GamesPlayed);
        Assert.Equal(3, await CountedGames());
    }

    [Fact]
    public async Task AGameSavedDuringARebuild_IsRequeued_AndCountedOnceAfterwards()
    {
        var token = await Cache.TryStartRebuildAsync(Week); // another pod is rebuilding the week
        Assert.NotNull(token);
        var game = Game(Utc(18, 19, 0), P(1, "alice", 4, 5, 1));

        await Assert.ThrowsAsync<CacheUnavailableException>(() => Save(game, Cache));
        await using (var db = fx.NewContext())
            Assert.Equal(1, await db.GameStats.CountAsync()); // Postgres has it already

        // That pod read Postgres before the game was committed, and finishes its rebuild without it.
        Assert.True(await Cache.CompleteRebuildAsync(Week, token, [], [], WeeklyLeaderboardService.MinGamesForRates));

        // The message comes back: Postgres says Duplicate, Redis counts it now.
        Assert.Equal(SaveResult.Duplicate, await Save(game, Cache));
        Assert.Equal(1, (await Totals(1)).GamesPlayed);
        await AssertSameAsPostgres(await Board(Cache));
    }

    [Fact]
    public async Task ARebuildThatLostItsLock_ChangesNothing()
    {
        var token = await Cache.TryStartRebuildAsync(Week);
        Assert.NotNull(token);
        Assert.Null(await Cache.TryStartRebuildAsync(Week)); // only one pod rebuilds at a time

        Assert.False(await Cache.CompleteRebuildAsync(Week, "someone-else", [], [], WeeklyLeaderboardService.MinGamesForRates));
        Assert.False(await fx.Redis.GetDatabase().KeyExistsAsync(RedisLeaderboardCache.Key(Week, "ready")));

        Assert.True(await Cache.CompleteRebuildAsync(Week, token, [], [], WeeklyLeaderboardService.MinGamesForRates));
        Assert.True(await fx.Redis.GetDatabase().KeyExistsAsync(RedisLeaderboardCache.Key(Week, "ready")));
        Assert.False(await fx.Redis.GetDatabase().KeyExistsAsync(RedisLeaderboardCache.Key(Week, "building")));
    }

    // ---- SCRUM-197 load-bot cleanup ----

    // Deleting rows in Postgres must not leave them in the cache: the cache only ever adds.
    [Fact]
    public async Task DeletingTheLoadBots_AlsoRemovesThemFromTheCachedLeaderboard()
    {
        await WarmWeek();
        await Save(Game(Utc(18, 19, 0), P(1, "alice", 4, 5, 2), P(90, "loadbot_1", 5, 5, 1)), Cache);
        await Save(Game(Utc(19, 19, 0), P(91, "loadbot_2", 5, 5, 1), P(92, "loadbot_3", 1, 5, 2)), Cache);
        var before = await Board(Cache);
        Assert.Contains(before.MostWins, w => w.UserName.StartsWith("loadbot_"));

        await using (var db = fx.NewContext())
            Assert.Equal(3, await new AdminStatsService(new AdminStatsRepository(db), Cache).DeleteLoadBotStatsAsync("loadbot_", CancellationToken.None));

        var after = await Board(Cache); // miss: rebuilt from Postgres, without the bots
        Assert.DoesNotContain(after.MostWins, w => w.UserName.StartsWith("loadbot_"));
        Assert.Empty(after.MostWins); // alice has no win
        await AssertSameAsPostgres(after);
        await AssertSameAsPostgres(await Board(Cache)); // and the next read is a hit with the same answer
    }

    [Fact]
    public async Task InvalidatingTheCache_CancelsARebuildThatIsRunning()
    {
        var token = await Cache.TryStartRebuildAsync(Week);
        Assert.NotNull(token);

        await Cache.InvalidateAllAsync();

        // That rebuild may have read the rows that were just deleted: it must not be stored.
        Assert.False(await Cache.CompleteRebuildAsync(Week, token, [], [], WeeklyLeaderboardService.MinGamesForRates));
    }

    // ---- Redis down ----

    [Fact]
    public async Task WhenRedisIsDown_TheLeaderboardComesFromPostgres_AndTheMessageIsRequeuedUntilRedisCounts()
    {
        await WarmWeek();
        await using var dead = await ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=300,asyncTimeout=300,syncTimeout=300");
        var deadCache = new RedisLeaderboardCache(dead);
        var game = Game(Utc(18, 19, 0), P(1, "alice", 4, 5, 1));

        await Assert.ThrowsAsync<CacheUnavailableException>(() => Save(game, deadCache));
        await using (var db = fx.NewContext())
            Assert.Equal(1, await db.GameStats.CountAsync()); // saved before Redis was tried

        await AssertSameAsPostgres(await Board(deadCache)); // fallback

        // Redis is back and the message is delivered again.
        Assert.Equal(SaveResult.Duplicate, await Save(game, Cache));
        Assert.Equal(1, (await Totals(1)).GamesPlayed);
    }

    // ---- ranking ----

    // More players than fit in a top 10, many of them tied at the cut-off: Redis returns only the
    // possible top players, and the ranking from them must equal the ranking over the whole week.
    [Fact]
    public async Task WithManyTiedPlayers_RedisGivesTheSameTop10AsPostgres()
    {
        await WarmWeek();
        const int players = 25;
        for (var k = 0; k < 90; k++)
        {
            var a = k % players + 1;
            var b = (k * 7 + 3) % players + 1;
            if (b == a) b = b % players + 1;
            var end = Utc(16 + k % 7, 10, k % 60);
            await Save(Game(end, P(a, $"p{a:00}", k % 5, 5, 1), P(b, $"P{b:00}", (k + 2) % 5, 5, k % 3 == 0 ? 1 : 2)), Cache);
        }

        var cached = await Board(Cache);
        Assert.Equal(WeeklyLeaderboardService.TopCount, cached.MostWins.Count);
        Assert.Equal(WeeklyLeaderboardService.TopCount, cached.WinRate.Count);
        await AssertSameAsPostgres(cached);

        await fx.FlushRedisAsync();
        await Board(Cache); // rebuild
        await AssertSameAsPostgres(await Board(Cache));
    }

    // ---- week key ----

    [Theory]
    [InlineData("2026-11-22T22:59:00Z", "2026-W47")] // Sunday 23:59 Danish time
    [InlineData("2026-11-22T23:00:00Z", "2026-W48")] // Monday 00:00 Danish time
    [InlineData("2026-12-31T23:30:00Z", "2026-W53")] // 1 January 00:30 Danish time is still ISO week 2026-W53
    public void AGame_BelongsToTheWeekItEndedIn_InDanishTime(string endUtc, string week)
    {
        var end = DateTime.Parse(endUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(week, WeeklyPeriod.Containing(end).Key);
    }
}
