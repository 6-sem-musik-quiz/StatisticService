using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StatisticService.Domain.DTOs;
using StatisticService.Service.Messaging;
using StatisticService.Service.Metrics;
using StatisticService.Service.Results;

namespace StatisticService.Service.Tests.Metrics;

// SCRUM-190: slopster_game_results_total counts every GameFinished message by what happened to it.
public class StatisticsMetricsTests : IDisposable
{
    private const string Valid =
        """{"gameId":"2096a3bf-9b62-40e3-b8b2-2242a5ee12d0","rounds":5,"startTime":"2026-11-25T14:00:00Z","endTime":"2026-11-25T14:10:00Z","catId":2,"categoryName":"Rock","players":[{"userId":1,"userName":"Alice","score":5,"correctAnswers":4,"answeredRounds":5,"placement":1}]}""";

    private readonly IGameResultService _service = Substitute.For<IGameResultService>();
    private readonly StatisticsMetrics _metrics = new();
    private readonly GameFinishedHandler _handler;
    private readonly MeterListener _listener = new();
    private readonly List<string?> _results = [];

    public StatisticsMetricsTests()
    {
        var provider = new ServiceCollection().AddScoped(_ => _service).BuildServiceProvider();
        _handler = new GameFinishedHandler(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GameFinishedHandler>.Instance, _metrics);

        // Only this test's meter: other tests may create their own at the same time.
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == _metrics.Meter) listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            Assert.Equal(StatisticsMetrics.GameResultsName, instrument.Name);
            Assert.Equal(1, value);
            foreach (var tag in tags)
                if (tag.Key == "result") _results.Add(tag.Value as string);
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
    }

    private Task Handle(string json) => _handler.HandleAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);

    [Fact]
    public async Task EveryOutcome_IsCountedWithItsResult()
    {
        _service.SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>())
            .Returns(SaveResult.Saved, SaveResult.Duplicate);
        await Handle(Valid);
        await Handle(Valid);

        await Handle("not json");

        _service.SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidGameResultException("rounds must be positive"));
        await Handle(Valid);

        _service.SaveAsync(Arg.Any<GameFinishedMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("database down"));
        await Handle(Valid);

        Assert.Equal(["saved", "duplicate", "invalid", "invalid", "retry"], _results);
    }

    [Fact]
    public void EveryResult_StartsAtZero_SoTheFirstGameShowsAsAnIncrease()
    {
        var seen = new List<(Meter Meter, long Value, string? Result)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == StatisticsMetrics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? result = null;
            foreach (var tag in tags)
                if (tag.Key == "result") result = tag.Value as string;
            lock (seen) seen.Add((instrument.Meter, value, result));
        });
        listener.Start();

        using var metrics = new StatisticsMetrics();

        List<(long, string?)> mine;
        lock (seen) mine = [.. seen.Where(m => m.Meter == metrics.Meter).Select(m => (m.Value, m.Result))];
        Assert.Equal([(0L, "saved"), (0L, "duplicate"), (0L, "invalid"), (0L, "retry")], mine);
    }
}
