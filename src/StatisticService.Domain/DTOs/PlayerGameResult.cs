namespace StatisticService.Domain.DTOs;

// One player's result in one finished game. The projection the weekly leaderboard reads;
// EndTime is kept so the newest user name can win if a player was renamed during the week.
public record PlayerGameResult(
    int UserId,
    string UserName,
    int Placement,
    int CorrectAnswers,
    int AnsweredRounds,
    DateTime EndTime);
