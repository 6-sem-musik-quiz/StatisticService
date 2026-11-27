using System.Diagnostics.Metrics;

namespace StatisticService.Service.Metrics;

// SCRUM-195: how the leaderboard cache is doing (meter "Slopster.Statistics", exported over OTLP).
//   slopster_leaderboard_cache_reads_total{result=hit|miss|fallback}
//     hit: answered from Redis. miss: the week was not in Redis, read from Postgres and rebuilt.
//     fallback: Redis did not answer, read from Postgres.
//   slopster_leaderboard_cache_writes_total{result=applied|already_applied|week_not_cached|rebuilding|failed}
//     one per saved GameFinished message: what the write-through to Redis did.
// Hit ratio in Grafana:
//   sum(rate(slopster_leaderboard_cache_reads_total{result="hit"}[5m])) / sum(rate(slopster_leaderboard_cache_reads_total[5m]))
// Observable counters, for the same reason as StatisticsMetrics: every result is exported from 0.
public sealed class LeaderboardCacheMetrics : IDisposable
{
    public const string ReadsName = "slopster_leaderboard_cache_reads_total";
    public const string WritesName = "slopster_leaderboard_cache_writes_total";

    public static readonly IReadOnlyList<string> ReadResults = ["hit", "miss", "fallback"];
    public static readonly IReadOnlyList<string> WriteResults = ["applied", "already_applied", "week_not_cached", "rebuilding", "failed"];

    private readonly long[] _reads = new long[ReadResults.Count];
    private readonly long[] _writes = new long[WriteResults.Count];

    public LeaderboardCacheMetrics()
    {
        Meter = new Meter(StatisticsMetrics.MeterName);
        Meter.CreateObservableCounter(ReadsName, () => Observe(ReadResults, _reads),
            description: "Weekly leaderboard reads, by where the answer came from.");
        Meter.CreateObservableCounter(WritesName, () => Observe(WriteResults, _writes),
            description: "Write-through of saved games to the leaderboard cache, by outcome.");
    }

    public Meter Meter { get; }

    public void Read(string result) => Interlocked.Increment(ref _reads[IndexOf(ReadResults, result)]);

    public void Write(string result) => Interlocked.Increment(ref _writes[IndexOf(WriteResults, result)]);

    private static IEnumerable<Measurement<long>> Observe(IReadOnlyList<string> results, long[] counts)
    {
        for (var i = 0; i < results.Count; i++)
            yield return new Measurement<long>(Interlocked.Read(ref counts[i]), new KeyValuePair<string, object?>("result", results[i]));
    }

    private static int IndexOf(IReadOnlyList<string> results, string result)
    {
        for (var i = 0; i < results.Count; i++)
            if (results[i] == result) return i;
        throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown cache result.");
    }

    public void Dispose() => Meter.Dispose();
}
