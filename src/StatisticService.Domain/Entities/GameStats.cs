namespace StatisticService.Domain.Entities;

// One finished game, saved from the GameFinished message.
public class GameStats
{
    // The gameId from GameService. Saving the same game twice hits this key.
    public Guid Id { get; set; }

    public int Rounds { get; set; }
    public DateTime StartTime { get; set; }  // UTC
    public DateTime EndTime { get; set; }    // UTC

    // Category (playlist) in MusicDB. No foreign key: it lives in another database.
    public int CatId { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    public List<UserStats> Players { get; set; } = [];
}
