using NSubstitute;
using StatisticService.Repository.Interfaces;
using StatisticService.Service.Services;

namespace StatisticService.Service.Tests;

public class MyStatsServiceTests
{
    private const int Me = 1;
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IMyStatsRepository _repo = Substitute.For<IMyStatsRepository>();
    private readonly MyStatsService _sut;

    public MyStatsServiceTests() => _sut = new MyStatsService(_repo);

    // A game that ended `dayOffset` days after T0 and lasted `minutes` minutes.
    private static MyGameRow Game(Guid id, int catId, string cat, int dayOffset, int minutes = 10,
        int rounds = 10, int correct = 5, int answered = 10, int placement = 2)
    {
        var end = T0.AddDays(dayOffset);
        return new MyGameRow(id, rounds, end.AddMinutes(-minutes), end, catId, cat, correct, answered, placement);
    }

    private void Setup(IEnumerable<MyGameRow> mine, params CoPlayerRow[] others)
    {
        _repo.GetMyGamesAsync(Me, Arg.Any<CancellationToken>()).Returns(mine.ToList());
        _repo.GetCoPlayersAsync(Me, Arg.Any<CancellationToken>()).Returns(others.ToList());
    }

    [Fact]
    public async Task AllFields_AreAggregatedFromMyGames()
    {
        var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid(); var g3 = Guid.NewGuid();
        Setup(
            [
                Game(g1, 7, "Rock", 0, minutes: 10, rounds: 10, correct: 8, answered: 10, placement: 1),
                Game(g2, 7, "Rock", 1, minutes: 20, rounds: 5, correct: 2, answered: 5, placement: 3),
                Game(g3, 9, "Pop", 2, minutes: 5, rounds: 15, correct: 5, answered: 15, placement: 1),
            ],
            new CoPlayerRow(g1, 2, "Alice"), new CoPlayerRow(g2, 2, "Alice"), new CoPlayerRow(g3, 3, "Bob"));

        var r = await _sut.GetMyStatsAsync(Me);

        Assert.Equal(3, r.GamesPlayed);
        Assert.Equal(2, r.Wins);
        Assert.Equal(2.0 / 3, r.WinRate, 10);
        Assert.Equal(15.0 / 30, r.Accuracy, 10);
        Assert.Equal(30, r.RoundsPlayed);
        Assert.Equal((10 + 20 + 5) * 60, r.TimePlayedSeconds);
        Assert.Equal((7, "Rock", 2), (r.MostPlayedCategory!.CategoryId, r.MostPlayedCategory.CategoryName, r.MostPlayedCategory.Games));
        Assert.Equal((2, "Alice", 2), (r.MostCommonFriend!.UserId, r.MostCommonFriend.UserName, r.MostCommonFriend.GamesTogether));
    }

    [Fact]
    public async Task NoGames_ReturnsZerosAndNulls()
    {
        Setup([]);

        var r = await _sut.GetMyStatsAsync(Me);

        Assert.Equal(0, r.GamesPlayed);
        Assert.Equal(0, r.Wins);
        Assert.Equal(0, r.WinRate);
        Assert.Equal(0, r.Accuracy);
        Assert.Equal(0, r.RoundsPlayed);
        Assert.Equal(0, r.TimePlayedSeconds);
        Assert.Null(r.MostPlayedCategory);
        Assert.Null(r.MostCommonFriend);
    }

    [Fact]
    public async Task CategoryTie_NewestGameWins()
    {
        Setup([
            Game(Guid.NewGuid(), 1, "Old", 0),
            Game(Guid.NewGuid(), 1, "Old", 1),
            Game(Guid.NewGuid(), 2, "New", 2),
            Game(Guid.NewGuid(), 2, "New", 5),
        ]);

        var r = await _sut.GetMyStatsAsync(Me);

        Assert.Equal(2, r.MostPlayedCategory!.CategoryId);
        Assert.Equal(2, r.MostPlayedCategory.Games);
    }

    [Fact]
    public async Task CategoryWithMoreGames_BeatsNewerCategory()
    {
        Setup([
            Game(Guid.NewGuid(), 1, "Many", 0),
            Game(Guid.NewGuid(), 1, "Many", 1),
            Game(Guid.NewGuid(), 2, "Newer", 9),
        ]);

        Assert.Equal(1, (await _sut.GetMyStatsAsync(Me)).MostPlayedCategory!.CategoryId);
    }

    [Fact]
    public async Task FriendTie_NewestSharedGameWins_AndNameComesFromThatGame()
    {
        var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid(); var g3 = Guid.NewGuid(); var g4 = Guid.NewGuid();
        Setup(
            [Game(g1, 1, "A", 0), Game(g2, 1, "A", 1), Game(g3, 1, "A", 2), Game(g4, 1, "A", 3)],
            new CoPlayerRow(g1, 2, "Alice"), new CoPlayerRow(g2, 2, "Alice"),
            new CoPlayerRow(g3, 3, "Bobby"), new CoPlayerRow(g4, 3, "Bob-renamed"));

        var friend = (await _sut.GetMyStatsAsync(Me)).MostCommonFriend!;

        Assert.Equal(3, friend.UserId);
        Assert.Equal(2, friend.GamesTogether);
        Assert.Equal("Bob-renamed", friend.UserName);
    }

    [Fact]
    public async Task Friend_IgnoresMyOwnRow_AndGamesWithoutOthers()
    {
        var g1 = Guid.NewGuid();
        Setup([Game(g1, 1, "A", 0)], new CoPlayerRow(g1, Me, "Me"));

        Assert.Null((await _sut.GetMyStatsAsync(Me)).MostCommonFriend);
    }

    [Fact]
    public async Task NoAnsweredRounds_GivesZeroAccuracy()
    {
        Setup([Game(Guid.NewGuid(), 1, "A", 0, correct: 0, answered: 0)]);

        var r = await _sut.GetMyStatsAsync(Me);

        Assert.Equal(0, r.Accuracy);
        Assert.Equal(1, r.GamesPlayed);
    }
}
