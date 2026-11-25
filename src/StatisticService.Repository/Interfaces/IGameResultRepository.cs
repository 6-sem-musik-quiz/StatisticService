using StatisticService.Domain.Entities;

namespace StatisticService.Repository.Interfaces;

public interface IGameResultRepository
{
    // Saves the game and its players. Returns false, and changes nothing, if a game with
    // the same id is already saved (the same message delivered twice).
    Task<bool> TryAddAsync(GameStats game, CancellationToken ct = default);
}
