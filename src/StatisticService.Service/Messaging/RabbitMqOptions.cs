namespace StatisticService.Service.Messaging;

// Section "RabbitMQ". In the cluster Host/Username/Password come from the
// statisticservice-secrets secret (gitops) as RabbitMQ__Host etc.
public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    // Off in Development and tests, so a local run never takes messages from the real queue.
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    // Must match what GameService publishes (SCRUM-83/84).
    public string ExchangeName { get; set; } = "game-events";
    public string RoutingKey { get; set; } = "game.finished.v1";

    // Owned by this service. Durable, so results wait here while every pod is down.
    public string QueueName { get; set; } = "statisticservice.game-finished";

    public ushort PrefetchCount { get; set; } = 10;
    public int ReconnectDelaySeconds { get; set; } = 5;
}
