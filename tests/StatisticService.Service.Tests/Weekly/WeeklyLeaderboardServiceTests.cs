using NSubstitute;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Interfaces;
using StatisticService.Service.Weekly;

namespace StatisticService.Service.Tests.Weekly;

public class WeeklyLeaderboardServiceTests
{
    private static readonly DateTime Tuesday = new(2026, 11, 17, 18, 0, 0, DateTimeKind.Utc);

    private static WeeklyPeriod Week47()
    {
        Assert.True(WeeklyPeriod.TryParse("2026-W47", DateTimeOffset.UtcNow, out var period));
        return period;
    }

    // `count` games for one player. `placements` is cycled; correct/answered are per game.
    private static IEnumerable<PlayerGameResult> Games(
        int userId, string name, int count, int placement = 2, int correct = 5, int answered = 10, DateTime? endTime = null)
        => Enumerable.Range(0, count)
            .Select(i => new PlayerGameResult(userId, name, placement, correct, answered, (endTime ?? Tuesday).AddMinutes(i)));

    private static IEnumerable<PlayerGameResult> GamesWithWins(int userId, string name, int wins, int losses, int correct = 5, int answered = 10)
        => Games(userId, name, wins, placement: 1, correct, answered)
            .Concat(Games(userId, name, losses, placement: 2, correct, answered));

    private static WeeklyLeaderboardResponse Build(params IEnumerable<PlayerGameResult>[] results)
        => WeeklyLeaderboardService.Build(Week47(), results.SelectMany(r => r));

    // ----- minimum number of games -----

    [Fact]
    public void WinRateAndAccuracy_NeedAtLeastFiveGames_MostWinsDoesNot()
    {
        var response = Build(
            GamesWithWins(1, "four", wins: 4, losses: 0, correct: 10, answered: 10),   // 4 games: perfect, but too few
            GamesWithWins(2, "five", wins: 2, losses: 3, correct: 5, answered: 10));   // 5 games: just enough

        Assert.Equal([1, 2], response.MostWins.Select(e => e.UserId));
        Assert.Equal([2], response.WinRate.Select(e => e.UserId));
        Assert.Equal([2], response.Accuracy.Select(e => e.UserId));
    }

    [Fact]
    public void PlayerWithOneWinAndOneGame_IsOnMostWinsOnly()
    {
        var response = Build(Games(1, "lucky", 1, placement: 1));

        Assert.Single(response.MostWins);
        Assert.Empty(response.WinRate);
        Assert.Empty(response.Accuracy);
    }

    [Fact]
    public void PlayersWithoutWins_AreNotOnMostWins_ButStillOnTheRateLists()
    {
        var response = Build(Games(1, "loser", 5, placement: 3, correct: 3, answered: 10));

        Assert.Empty(response.MostWins);
        Assert.Single(response.WinRate);
        Assert.Equal(0, response.WinRate[0].WinRate);
        Assert.Single(response.Accuracy);
    }

    // ----- wins, shared first place, rates -----

    [Fact]
    public void SharedFirstPlace_CountsAsAWinForEveryoneWithPlacementOne()
    {
        var gameEnd = Tuesday;
        var response = Build(
        [
            new PlayerGameResult(1, "ann", 1, 8, 10, gameEnd),
            new PlayerGameResult(2, "bob", 1, 8, 10, gameEnd),
            new PlayerGameResult(3, "cat", 3, 2, 10, gameEnd)
        ]);

        Assert.Equal([1, 2], response.MostWins.Select(e => e.UserId));
        Assert.All(response.MostWins, e => Assert.Equal(1, e.Wins));
    }

    [Fact]
    public void WinRate_IsWinsDividedByGamesPlayed()
    {
        var response = Build(GamesWithWins(1, "ann", wins: 3, losses: 2));

        var entry = Assert.Single(response.WinRate);
        Assert.Equal(0.6, entry.WinRate, 10);
        Assert.Equal(5, entry.GamesPlayed);
        Assert.Equal(3, Assert.Single(response.MostWins).Wins);
    }

    [Fact]
    public void Accuracy_IsSumOfCorrectDividedBySumOfAnswered_NotAnAverageOfGameAccuracies()
    {
        // Game accuracies: 1/1 = 100% and 0/9 = 0%. Average would be 50%, the pooled value is 1/10.
        var results = Games(1, "ann", 4, correct: 0, answered: 0)
            .Append(new PlayerGameResult(1, "ann", 2, 1, 1, Tuesday))
            .Append(new PlayerGameResult(1, "ann", 2, 0, 9, Tuesday));

        var response = Build(results);

        Assert.Equal(0.1, Assert.Single(response.Accuracy).Accuracy, 10);
    }

    [Fact]
    public void Accuracy_IsZero_WhenNothingWasAnswered()
    {
        var response = Build(Games(1, "ann", 5, correct: 0, answered: 0));

        var entry = Assert.Single(response.Accuracy);
        Assert.Equal(0, entry.Accuracy);
        Assert.False(double.IsNaN(entry.Accuracy));
    }

    // ----- sorting -----

    [Fact]
    public void Lists_AreSortedDescendingByValue()
    {
        var response = Build(
            GamesWithWins(1, "low", wins: 1, losses: 4, correct: 2, answered: 10),
            GamesWithWins(2, "high", wins: 4, losses: 1, correct: 9, answered: 10),
            GamesWithWins(3, "mid", wins: 2, losses: 3, correct: 5, answered: 10));

        Assert.Equal([2, 3, 1], response.MostWins.Select(e => e.UserId));
        Assert.Equal([2, 3, 1], response.WinRate.Select(e => e.UserId));
        Assert.Equal([2, 3, 1], response.Accuracy.Select(e => e.UserId));
    }

    [Fact]
    public void Tie_MostWins_ThenMostGames_ThenUserName()
    {
        var response = Build(
            GamesWithWins(1, "zed", wins: 2, losses: 0),      // 2 wins, 2 games
            GamesWithWins(2, "mia", wins: 2, losses: 3),      // 2 wins, 5 games -> first (most games)
            GamesWithWins(3, "bob", wins: 2, losses: 0),      // 2 wins, 2 games, name before zed
            GamesWithWins(4, "Anna", wins: 2, losses: 0));    // 2 wins, 2 games, name first

        Assert.Equal([2, 4, 3, 1], response.MostWins.Select(e => e.UserId));
    }

    [Fact]
    public void Tie_WinRate_ThenMostGames_ThenUserName()
    {
        var response = Build(
            GamesWithWins(1, "zed", wins: 3, losses: 3),      // 50%, 6 games
            GamesWithWins(2, "mia", wins: 5, losses: 5),      // 50%, 10 games -> first
            GamesWithWins(3, "bob", wins: 3, losses: 3));     // 50%, 6 games, name before zed

        Assert.Equal([2, 3, 1], response.WinRate.Select(e => e.UserId));
    }

    [Fact]
    public void Tie_Accuracy_ThenMostGames_ThenUserName()
    {
        var response = Build(
            Games(1, "zed", 5, correct: 7, answered: 10),
            Games(2, "mia", 8, correct: 7, answered: 10),
            Games(3, "bob", 5, correct: 7, answered: 10));

        Assert.Equal([2, 3, 1], response.Accuracy.Select(e => e.UserId));
    }

    [Fact]
    public void Tie_OnValueFromDifferentFractions_IsStillATie()
    {
        // 3/6 and 5/10 are both exactly 0.5, so the game count decides.
        var response = Build(
            GamesWithWins(1, "ann", wins: 3, losses: 3),
            GamesWithWins(2, "bob", wins: 5, losses: 5));

        Assert.Equal([2, 1], response.WinRate.Select(e => e.UserId));
    }

    [Fact]
    public void UserNameTieBreak_IsOrdinal_NotCultureSensitive()
    {
        // Culture-aware comparison would put "aa" after "å" in Danish; ordinal puts 'a' (97) before 'å' (229).
        var response = Build(
            GamesWithWins(1, "åke", wins: 1, losses: 0),
            GamesWithWins(2, "aa", wins: 1, losses: 0));

        Assert.Equal([2, 1], response.MostWins.Select(e => e.UserId));
    }

    // ----- top 10 -----

    [Fact]
    public void EachList_IsCutToTheTenBest()
    {
        var players = Enumerable.Range(1, 12)
            .SelectMany(i => GamesWithWins(i, $"p{i:00}", wins: i, losses: 0).Concat(Games(i, $"p{i:00}", 5, placement: 2, correct: i, answered: 20)));

        var response = Build(players);

        Assert.Equal(10, response.MostWins.Count);
        Assert.Equal(10, response.WinRate.Count);
        Assert.Equal(10, response.Accuracy.Count);
        Assert.Equal(12, response.MostWins[0].UserId);
        Assert.DoesNotContain(response.MostWins, e => e.UserId is 1 or 2);
    }

    // ----- grouping and names -----

    [Fact]
    public void ARenamedPlayer_IsOneEntry_WithTheNameFromTheLatestGame()
    {
        var response = Build(
            Games(1, "oldname", 3, placement: 1, endTime: Tuesday),
            Games(1, "newname", 2, placement: 1, endTime: Tuesday.AddDays(2)));

        var entry = Assert.Single(response.MostWins);
        Assert.Equal("newname", entry.UserName);
        Assert.Equal(5, entry.Wins);
        Assert.Equal(5, entry.GamesPlayed);
    }

    [Fact]
    public void EmptyWeek_GivesThreeEmptyLists()
    {
        var response = Build();

        Assert.Empty(response.MostWins);
        Assert.Empty(response.WinRate);
        Assert.Empty(response.Accuracy);
    }

    [Fact]
    public void Response_CarriesTheWeekBounds()
    {
        var period = Week47();

        var response = WeeklyLeaderboardService.Build(period, []);

        Assert.Equal(period.WeekStart, response.WeekStart);
        Assert.Equal(period.WeekEnd, response.WeekEnd);
    }

    // ----- GetAsync: parsing, time and repository -----

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (WeeklyLeaderboardService Service, IWeeklyStatsRepository Repository) CreateService(string nowUtc = "2026-11-18T10:00:00Z")
    {
        var repository = Substitute.For<IWeeklyStatsRepository>();
        repository.GetPlayerResultsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PlayerGameResult>());
        return (new WeeklyLeaderboardService(repository, new FixedTimeProvider(DateTimeOffset.Parse(nowUtc))), repository);
    }

    [Fact]
    public async Task GetAsync_Current_QueriesTheWeekContainingNow()
    {
        var (service, repository) = CreateService("2026-11-18T10:00:00Z");

        var response = await service.GetAsync("current");

        await repository.Received(1).GetPlayerResultsAsync(
            new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(1)), response.WeekStart);
    }

    [Fact]
    public async Task GetAsync_SpecificWeek_QueriesThatWeek()
    {
        var (service, repository) = CreateService();

        await service.GetAsync("2026-W13");

        await repository.Received(1).GetPlayerResultsAsync(
            new DateTime(2026, 3, 22, 23, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("2026-W99")]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetAsync_InvalidWeek_Throws_AndDoesNotTouchTheDatabase(string? week)
    {
        var (service, repository) = CreateService();

        await Assert.ThrowsAsync<InvalidWeekException>(() => service.GetAsync(week));

        await repository.DidNotReceiveWithAnyArgs().GetPlayerResultsAsync(default, default, default);
    }
}
