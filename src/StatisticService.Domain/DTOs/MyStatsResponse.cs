namespace StatisticService.Domain.DTOs;

// Response for GET /api/statistics/me. WinRate and Accuracy are fractions 0-1.
public record MyStatsResponse(
    int GamesPlayed,
    int Wins,
    double WinRate,
    double Accuracy,
    int RoundsPlayed,
    long TimePlayedSeconds,
    MostPlayedCategoryDto? MostPlayedCategory,
    MostCommonFriendDto? MostCommonFriend);

public record MostPlayedCategoryDto(int CategoryId, string CategoryName, int Games);

public record MostCommonFriendDto(int UserId, string UserName, int GamesTogether);
