namespace StatisticService.Domain.DTOs;

// The GameFinished v1 message GameService publishes on RabbitMQ (contract: SCRUM-83).
// Exchange "game-events" (topic), routing key "game.finished.v1", camelCase JSON.
public class GameFinishedMessage
{
    public Guid GameId { get; set; }
    public int Rounds { get; set; }
    public DateTime StartTime { get; set; }  // UTC; equals EndTime if the game never started
    public DateTime EndTime { get; set; }    // UTC
    public int CatId { get; set; }           // note: catId, not categoryId
    public string CategoryName { get; set; } = string.Empty;
    public List<GameFinishedPlayer> Players { get; set; } = [];
}

public class GameFinishedPlayer
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Score { get; set; }
    public int CorrectAnswers { get; set; }
    public int AnsweredRounds { get; set; }
    public int Placement { get; set; }
}
