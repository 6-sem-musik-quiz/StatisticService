using NSubstitute;
using StatisticService.Domain.DTOs;
using StatisticService.Domain.Entities;
using StatisticService.Repository.Interfaces;
using StatisticService.Service.Results;

namespace StatisticService.Service.Tests.Results;

public class GameResultServiceTests
{
    private readonly IGameResultRepository _repository = Substitute.For<IGameResultRepository>();
    private readonly GameResultService _service;

    public GameResultServiceTests()
    {
        _repository.TryAddAsync(Arg.Any<GameStats>(), Arg.Any<CancellationToken>()).Returns(true);
        _service = new GameResultService(_repository);
    }

    public static GameFinishedMessage ValidMessage() => new()
    {
        GameId = Guid.Parse("2096a3bf-9b62-40e3-b8b2-2242a5ee12d0"),
        Rounds = 5,
        StartTime = new DateTime(2026, 11, 25, 14, 0, 0, DateTimeKind.Utc),
        EndTime = new DateTime(2026, 11, 25, 14, 10, 0, DateTimeKind.Utc),
        CatId = 2,
        CategoryName = "Rock",
        Players =
        [
            new() { UserId = 1, UserName = "Alice", Score = 900, CorrectAnswers = 4, AnsweredRounds = 5, Placement = 1 },
            new() { UserId = 2, UserName = "Bob", Score = 400, CorrectAnswers = 2, AnsweredRounds = 4, Placement = 2 }
        ]
    };

    [Fact]
    public async Task Save_MapsEveryField()
    {
        GameStats? saved = null;
        _repository.TryAddAsync(Arg.Do<GameStats>(g => saved = g), Arg.Any<CancellationToken>()).Returns(true);
        var message = ValidMessage();

        var result = await _service.SaveAsync(message);

        Assert.Equal(SaveResult.Saved, result);
        Assert.NotNull(saved);
        Assert.Equal(message.GameId, saved.Id);
        Assert.Equal(5, saved.Rounds);
        Assert.Equal(message.StartTime, saved.StartTime);
        Assert.Equal(message.EndTime, saved.EndTime);
        Assert.Equal(2, saved.CatId);
        Assert.Equal("Rock", saved.CategoryName);
        Assert.Equal(2, saved.Players.Count);

        var alice = saved.Players.Single(p => p.UserId == 1);
        Assert.Equal(message.GameId, alice.GameId);
        Assert.Equal("Alice", alice.UserName);
        Assert.Equal(900, alice.Score);
        Assert.Equal(4, alice.CorrectAnswers);
        Assert.Equal(5, alice.AnsweredRounds);
        Assert.Equal(1, alice.Placement);
    }

    [Fact]
    public async Task Save_SameGameTwice_ReturnsDuplicate()
    {
        _repository.TryAddAsync(Arg.Any<GameStats>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.Equal(SaveResult.Duplicate, await _service.SaveAsync(ValidMessage()));
    }

    [Fact]
    public async Task Save_TimeWithoutZone_IsStoredAsUtc()
    {
        GameStats? saved = null;
        _repository.TryAddAsync(Arg.Do<GameStats>(g => saved = g), Arg.Any<CancellationToken>()).Returns(true);
        var message = ValidMessage();
        message.StartTime = DateTime.SpecifyKind(message.StartTime, DateTimeKind.Unspecified);
        message.EndTime = DateTime.SpecifyKind(message.EndTime, DateTimeKind.Unspecified);

        await _service.SaveAsync(message);

        Assert.Equal(DateTimeKind.Utc, saved!.StartTime.Kind);
        Assert.Equal(DateTimeKind.Utc, saved.EndTime.Kind);
    }

    [Fact]
    public async Task Save_GameThatNeverStarted_IsAccepted()
    {
        var message = ValidMessage();
        message.StartTime = message.EndTime;

        Assert.Equal(SaveResult.Saved, await _service.SaveAsync(message));
    }

    public static TheoryData<string> InvalidCases() =>
    [
        "emptyGameId", "zeroRounds", "endBeforeStart", "noCategory", "longCategory",
        "noPlayers", "duplicateUser", "longUserName", "emptyUserName", "moreCorrectThanAnswered", "zeroPlacement"
    ];

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task Save_InvalidMessage_ThrowsAndSavesNothing(string invalid)
    {
        var m = ValidMessage();
        switch (invalid)
        {
            case "emptyGameId": m.GameId = Guid.Empty; break;
            case "zeroRounds": m.Rounds = 0; break;
            case "endBeforeStart": m.EndTime = m.StartTime.AddMinutes(-1); break;
            case "noCategory": m.CategoryName = ""; break;
            case "longCategory": m.CategoryName = new string('x', 61); break;
            case "noPlayers": m.Players = []; break;
            case "duplicateUser": m.Players[1].UserId = m.Players[0].UserId; break;
            case "longUserName": m.Players[0].UserName = new string('x', 21); break;
            case "emptyUserName": m.Players[0].UserName = " "; break;
            case "moreCorrectThanAnswered": m.Players[0].CorrectAnswers = 6; break;
            case "zeroPlacement": m.Players[0].Placement = 0; break;
        }

        await Assert.ThrowsAsync<InvalidGameResultException>(() => _service.SaveAsync(m));
        await _repository.DidNotReceive().TryAddAsync(Arg.Any<GameStats>(), Arg.Any<CancellationToken>());
    }
}
