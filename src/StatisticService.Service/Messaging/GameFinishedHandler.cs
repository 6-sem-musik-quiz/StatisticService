using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StatisticService.Domain.DTOs;
using StatisticService.Service.Metrics;
using StatisticService.Service.Results;

namespace StatisticService.Service.Messaging;

public enum DeliveryOutcome
{
    Ack,      // saved, or already saved before
    Reject,   // invalid message: nack without requeue, it is dropped
    Requeue   // could not save right now (e.g. database down): nack with requeue
}

// Turns one delivery into a save and decides what to tell RabbitMQ. Kept apart from the
// RabbitMQ plumbing in ResultConsumer so it can be tested without a broker.
// metrics (SCRUM-190) counts what happened to each message; null in tests that do not look at it.
public class GameFinishedHandler(IServiceScopeFactory scopes, ILogger<GameFinishedHandler> logger, StatisticsMetrics? metrics = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DeliveryOutcome> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        GameFinishedMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<GameFinishedMessage>(body.Span, Json);
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Dropped a GameFinished message that is not valid JSON");
            metrics?.GameResult("invalid");
            return DeliveryOutcome.Reject;
        }

        if (message is null)
        {
            logger.LogWarning("Dropped an empty GameFinished message");
            metrics?.GameResult("invalid");
            return DeliveryOutcome.Reject;
        }

        try
        {
            // DbContext is scoped: one scope per message.
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IGameResultService>();
            var result = await service.SaveAsync(message, ct);

            if (result == SaveResult.Duplicate)
                logger.LogInformation("Game {GameId} was already saved; ignoring the repeated message", message.GameId);
            else
                logger.LogInformation("Saved game {GameId} with {Players} players", message.GameId, message.Players.Count);

            metrics?.GameResult(result == SaveResult.Duplicate ? "duplicate" : "saved");
            return DeliveryOutcome.Ack;
        }
        catch (InvalidGameResultException e)
        {
            logger.LogWarning("Dropped GameFinished for game {GameId}: {Reason}", message.GameId, e.Message);
            metrics?.GameResult("invalid");
            return DeliveryOutcome.Reject;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return DeliveryOutcome.Requeue;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not save game {GameId}; it goes back on the queue", message.GameId);
            metrics?.GameResult("retry");
            return DeliveryOutcome.Requeue;
        }
    }
}
