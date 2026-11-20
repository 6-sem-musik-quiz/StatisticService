namespace StatisticService.Domain.Entities;

// One player's result in one game.
public class UserStats
{
    public Guid GameId { get; set; }
    public GameStats Game { get; set; } = null!;

    // User in UserDB. No foreign key: it lives in another database.
    public int UserId { get; set; }

    // Copied here so leaderboards don't need to call UserService.
    public string UserName { get; set; } = string.Empty;

    public int Score { get; set; }

    // Accuracy is CorrectAnswers / AnsweredRounds, calculated when read.
    public int CorrectAnswers { get; set; }
    public int AnsweredRounds { get; set; }

    public int Placement { get; set; }
}
