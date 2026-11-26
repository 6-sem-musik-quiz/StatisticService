using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;

namespace StatisticService.Service.Messaging;

// SCRUM-173: one game is one trace. RabbitMQ.Client's own consumer span ("deliver") starts a new trace
// and only links to the publisher, as the OpenTelemetry messaging conventions suggest. We want the
// handling of GameFinished (its SQL and logs) inside the game's trace, so the consumer reads the
// traceparent GameService's publish put in the message headers and starts its span as a child of it.
public static class MessageTracing
{
    public const string SourceName = "Slopster.Statistics.Messaging";
    private static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartConsume(IReadOnlyBasicProperties? properties, string queue, string routingKey)
    {
        var parent = default(ActivityContext);
        if (properties?.Headers is { } headers)
        {
            DistributedContextPropagator.Current.ExtractTraceIdAndState(headers, ReadHeader, out var traceParent, out var traceState);
            ActivityContext.TryParse(traceParent, traceState, isRemote: true, out parent);
        }

        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        return activity;
    }

    // Header values arrive from RabbitMQ as byte[] (AMQP long strings).
    private static void ReadHeader(object? carrier, string name, out string? value, out IEnumerable<string>? values)
    {
        values = null;
        value = null;
        if (carrier is IDictionary<string, object?> headers && headers.TryGetValue(name, out var raw))
            value = raw switch { byte[] bytes => Encoding.UTF8.GetString(bytes), string text => text, _ => null };
    }
}
