using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace StatisticService.Service.Messaging;

// SCRUM-85: reads GameFinished messages from RabbitMQ and saves them in StatsDB.
//
// Declares the durable queue "statisticservice.game-finished" and binds it to the topic
// exchange "game-events" with "game.finished.v1". Every pod consumes from the same queue, so
// each message is handled by one pod. Messages are acked after they are saved, nacked without
// requeue when invalid, and nacked with requeue when the database fails.
//
// If RabbitMQ is unreachable the service keeps running (the HTTP API still works) and
// reconnects every ReconnectDelaySeconds.
public class ResultConsumer(
    IOptions<RabbitMqOptions> options,
    GameFinishedHandler handler,
    ILogger<ResultConsumer> logger,
    IConnectionFactory? connectionFactory = null) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;

    private readonly IConnectionFactory _factory = connectionFactory ?? new ConnectionFactory
    {
        HostName = options.Value.Host,
        Port = options.Value.Port,
        UserName = options.Value.Username,
        Password = options.Value.Password,
        VirtualHost = options.Value.VirtualHost,
        ClientProvidedName = "statisticservice",
        // Reconnecting is done by the loop below, which also declares the queue again.
        AutomaticRecoveryEnabled = false
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("RabbitMQ:Enabled is false; game results are not consumed");
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilDisconnectedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "RabbitMQ consumer failed ({Host}:{Port}); retrying in {Delay}s",
                    _options.Host, _options.Port, delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConsumeUntilDisconnectedAsync(CancellationToken stoppingToken)
    {
        await using var connection = await _factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, e) =>
        {
            closed.TrySetException(new InvalidOperationException($"Connection closed: {e.ReplyText}"));
            return Task.CompletedTask;
        };
        channel.ChannelShutdownAsync += (_, e) =>
        {
            closed.TrySetException(new InvalidOperationException($"Channel closed: {e.ReplyText}"));
            return Task.CompletedTask;
        };

        // Same declaration as GameService's publisher; declaring twice is harmless.
        await channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_options.QueueName,
            durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(_options.QueueName, _options.ExchangeName, _options.RoutingKey,
            cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(_options.QueueName, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming {Queue} (bound to {Exchange} / {RoutingKey})",
            _options.QueueName, _options.ExchangeName, _options.RoutingKey);

        // Runs until the connection drops (throws, so the loop reconnects) or the app stops.
        await closed.Task.WaitAsync(stoppingToken);
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var outcome = await handler.HandleAsync(delivery.Body, stoppingToken);
        if (stoppingToken.IsCancellationRequested) return; // unacked: RabbitMQ redelivers it

        switch (outcome)
        {
            case DeliveryOutcome.Ack:
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
                break;
            case DeliveryOutcome.Reject:
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
                break;
            case DeliveryOutcome.Requeue:
                // Wait a bit so a database outage does not turn into a tight redelivery loop.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
                break;
        }
    }
}
