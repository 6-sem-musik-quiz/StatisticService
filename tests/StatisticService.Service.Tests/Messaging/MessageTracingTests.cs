using System.Diagnostics;
using System.Text;
using StatisticService.Service.Messaging;
using RabbitMQ.Client;

namespace StatisticService.Service.Tests.Messaging;

// SCRUM-173: handling GameFinished joins the game's trace, from the traceparent header GameService sent.
public class MessageTracingTests : IDisposable
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736", PublishSpan = "00f067aa0ba902b7";
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name == MessageTracing.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
    };

    public MessageTracingTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void TheConsumerSpan_IsAChildOfThePublish_InTheGamesTrace()
    {
        // On the wire, RabbitMQ hands header strings back as bytes.
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?> { ["traceparent"] = Encoding.UTF8.GetBytes($"00-{TraceId}-{PublishSpan}-01") }
        };

        using var activity = MessageTracing.StartConsume(properties, "q", "game.finished.v1");

        Assert.NotNull(activity);
        Assert.Equal(TraceId, activity.TraceId.ToHexString());
        Assert.Equal(PublishSpan, activity.ParentSpanId.ToHexString());
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
        Assert.Equal("q process", activity.DisplayName);
    }

    [Fact]
    public void WithoutATraceparent_TheConsumerStartsItsOwnTrace()
    {
        using var activity = MessageTracing.StartConsume(new BasicProperties(), "q", "game.finished.v1");

        Assert.NotNull(activity);
        Assert.Equal(default, activity.ParentSpanId);
    }
}
