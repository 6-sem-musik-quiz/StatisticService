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
    private readonly Dictionary<string, long> _totals = [];

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
            foreach (var tag in tags)
                if (tag.Key == "result") _totals[(string)tag.Value!] = value;
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

        _listener.RecordObservableInstruments(); // what the exporter does at every export
        Assert.Equal(new Dictionary<string, long> { ["saved"] = 1, ["duplicate"] = 1, ["invalid"] = 2, ["retry"] = 1 }, _totals);
    }

    [Fact]
    public void EveryResult_IsExportedFromTheStart_AsZero()
    {
        _listener.RecordObservableInstruments();

        Assert.Equal(new Dictionary<string, long> { ["saved"] = 0, ["duplicate"] = 0, ["invalid"] = 0, ["retry"] = 0 }, _totals);
    }

    [Fact]
    public void AnUnknownResult_IsABug()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _metrics.GameResult("lost"));
    }
}
