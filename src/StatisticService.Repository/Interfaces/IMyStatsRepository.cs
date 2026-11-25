namespace StatisticService.Repository.Interfaces;

// One of my games, with only the fields "my stats" needs.
public record MyGameRow(
    Guid GameId,
    int Rounds,
    DateTime StartTime,
    DateTime EndTime,
    int CatId,
    string CategoryName,
    int CorrectAnswers,
    int AnsweredRounds,
    int Placement);

// Another player's row in one of my games.
public record CoPlayerRow(Guid GameId, int UserId, string UserName);

public interface IMyStatsRepository
{
    Task<IReadOnlyList<MyGameRow>> GetMyGamesAsync(int userId, CancellationToken ct = default);

    // The other players (not me) in every game I played.
    Task<IReadOnlyList<CoPlayerRow>> GetCoPlayersAsync(int userId, CancellationToken ct = default);
}
