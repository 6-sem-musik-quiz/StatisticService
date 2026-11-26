using System.Diagnostics.Metrics;
using StatisticService.Service.Messaging;

namespace StatisticService.Service.Metrics;

// SCRUM-190/173: StatisticService's own instruments (meter "Slopster.Statistics"), exported over OTLP.
//   slopster_game_results_total{result=saved|duplicate|invalid|retry}
//     one per GameFinished message: saved, already saved (the same game delivered again), dropped as
//     invalid, or sent back to the queue because it could not be saved now (e.g. the database is down).
public sealed class StatisticsMetrics : IDisposable
{
    public const string MeterName = "Slopster.Statistics";
    public const string GameResultsName = "slopster_game_results_total";

    private readonly Counter<long> _results;

    public StatisticsMetrics()
    {
        Meter = new Meter(MeterName);
        _results = Meter.CreateCounter<long>(GameResultsName, description: "GameFinished messages, by what happened to them.");
    }

    public Meter Meter { get; }

    public void GameResult(string result) =>
        _results.Add(1, new KeyValuePair<string, object?>("result", result));

    public void Dispose() => Meter.Dispose();
}
