using System.Diagnostics.Metrics;

namespace StatisticService.Service.Metrics;

// SCRUM-190/173: StatisticService's own instruments (meter "Slopster.Statistics"), exported over OTLP.
//   slopster_game_results_total{result=saved|duplicate|invalid|retry}
//     one per GameFinished message: saved, already saved (the same game delivered again), dropped as
//     invalid, or sent back to the queue because it could not be saved now (e.g. the database is down).
//
// An observable counter: the exporter reads all four totals at every export, zeros included. With a
// plain Counter a result only appears at its first Add, and Prometheus' increase() then never sees a
// pod's first game (there is no earlier sample). Adding 0 at startup does not help, because that runs
// before OpenTelemetry listens and is dropped.
public sealed class StatisticsMetrics : IDisposable
{
    public const string MeterName = "Slopster.Statistics";
    public const string GameResultsName = "slopster_game_results_total";

    public static readonly IReadOnlyList<string> Results = ["saved", "duplicate", "invalid", "retry"];

    private readonly long[] _counts = new long[Results.Count];

    public StatisticsMetrics()
    {
        Meter = new Meter(MeterName);
        Meter.CreateObservableCounter(GameResultsName, Observe, description: "GameFinished messages, by what happened to them.");
    }

    public Meter Meter { get; }

    public void GameResult(string result)
    {
        var index = IndexOf(result);
        Interlocked.Increment(ref _counts[index]);
    }

    private IEnumerable<Measurement<long>> Observe()
    {
        for (var i = 0; i < Results.Count; i++)
            yield return new Measurement<long>(Interlocked.Read(ref _counts[i]), new KeyValuePair<string, object?>("result", Results[i]));
    }

    private static int IndexOf(string result)
    {
        for (var i = 0; i < Results.Count; i++)
            if (Results[i] == result) return i;
        throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown game result.");
    }

    public void Dispose() => Meter.Dispose();
}
