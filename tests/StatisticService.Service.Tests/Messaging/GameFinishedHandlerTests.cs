using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;
using StatisticService.Domain.DTOs;
using StatisticService.Service.Messaging;
using StatisticService.Service.Results;

namespace StatisticService.Service.Tests.Messaging;

public class GameFinishedHandlerTests
{
    // Exactly what GameService's ResultPublisher sends (camelCase, UTC with "Z").
    private const string GameServiceJson =
        """{"gameId":"2096a3bf-9b62-40e3-b8b2-2242a5ee12d0","rounds":5,"startTime":"2026-11-25T14:00:00Z","endTime":"2026-11-25T14:10:00Z","catId":2,"categoryName":"Rock","players":[{"userId":1,"userName":"Alice","score":900,"correctAnswers":4,"answeredRounds":5,"placement":1},{"userId":2,"userName":"Bob","score":400,"correctAnswers":2,"answeredRounds":4,"placement":2}]}""";

    private readonly IGameResultService _service = Substitute.For<IGameResultService>();
    private readonly GameFinishedHandler _handler;

    public GameFinishedHandlerTests()
    {
        var provider = new ServiceCollection().AddScoped(_ => _service).BuildServiceProvider();
        _handler = new GameFinishedHandler(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GameFinishedHandler>.Instance);
    }

    private Task<DeliveryOutcome> Handle(string json) =>
        _handler.HandleAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);

    [Fact]
    public async Task GameServiceMessage_IsParsedSavedAndAcked()
    {
        GameFinishedMessage? received = null;
        _service.SaveAsync(Arg.Do<GameFinishedMessage>(m => received = m), Arg.Any<CancellationToken>())
            .Returns(SaveResult.Saved);

        Assert.Equal(DeliveryOutcome.Ack, await Handle(GameServiceJson));

        Assert.NotNull(received);
        Assert.Equal(Guid.Parse("2096a3bf-9b62-40e3-b8b2-2242a5ee12d0"), received.GameId);
        Assert.Equal(5, received.Rounds);
        Assert.Equal(new DateTime(2026, 11, 25, 14, 10, 0, DateTimeKind.Utc), received.EndTime);
        Assert.Equal(DateTimeKind.Utc, received.EndTime.Kind);
        Assert.Equal(2, received.CatId);
        Assert.Equal("Rock", received.CategoryName);
        Assert.Equal(2, received.Players.Count);
        Assert.Equal("Bob", received.Players[1].UserName);
        Assert.Equal(4, received.Players[1].AnsweredRounds);
        Assert.Equal(2, received.Players[1].Placement);
    }

    [Fact]
    public async Task Duplicate_IsAcked()
    {
        _service.SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>()).Returns(SaveResult.Duplicate);

        Assert.Equal(DeliveryOutcome.Ack, await Handle(GameServiceJson));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"gameId":"not-a-guid"}""")]
    public async Task UnreadableMessage_IsRejected(string json)
    {
        Assert.Equal(DeliveryOutcome.Reject, await Handle(json));
        await _service.DidNotReceive().SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidMessage_IsRejected()
    {
        _service.SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidGameResultException("players is empty"));

        Assert.Equal(DeliveryOutcome.Reject, await Handle(GameServiceJson));
    }

    [Fact]
    public async Task DatabaseError_IsRequeued()
    {
        _service.SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is down"));

        Assert.Equal(DeliveryOutcome.Requeue, await Handle(GameServiceJson));
    }

    // SCRUM-195: saved in Postgres, but Redis did not take it. The message must come back.
    [Fact]
    public async Task LeaderboardCacheError_IsRequeued()
    {
        _service.SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new StatisticService.Repository.Interfaces.CacheUnavailableException("Redis is not available"));

        Assert.Equal(DeliveryOutcome.Requeue, await Handle(GameServiceJson));
    }

    [Fact]
    public async Task Consumer_WhenDisabled_NeverConnects()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var consumer = new ResultConsumer(
            Options.Create(new RabbitMqOptions { Enabled = false }), _handler,
            NullLogger<ResultConsumer>.Instance, factory);

        await consumer.StartAsync(CancellationToken.None);
        await consumer.ExecuteTask!;
        await consumer.StopAsync(CancellationToken.None);

        await factory.DidNotReceive().CreateConnectionAsync(Arg.Any<CancellationToken>());
    }
}
