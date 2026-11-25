namespace StatisticService.Domain.DTOs;

// Response for GET /api/statistics/weekly. Serialised as camelCase by ASP.NET Core.
// WeekStart/WeekEnd carry the Copenhagen offset (+01:00 / +02:00) of that moment.
// WinRate and Accuracy are fractions between 0 and 1 (0.75 = 75%); the frontend formats them.
public record WeeklyLeaderboardResponse(
    DateTimeOffset WeekStart,
    DateTimeOffset WeekEnd,
    IReadOnlyList<MostWinsEntry> MostWins,
    IReadOnlyList<WinRateEntry> WinRate,
    IReadOnlyList<AccuracyEntry> Accuracy);

public record MostWinsEntry(int UserId, string UserName, int Wins, int GamesPlayed);

public record WinRateEntry(int UserId, string UserName, double WinRate, int GamesPlayed);

public record AccuracyEntry(int UserId, string UserName, double Accuracy, int GamesPlayed);
