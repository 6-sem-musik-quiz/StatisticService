using StatisticService.Domain.DTOs;
using StatisticService.Repository.Interfaces;
using StatisticService.Service.Interfaces;

namespace StatisticService.Service.Services;

public class MyStatsService(IMyStatsRepository repository) : IMyStatsService
{
    public async Task<MyStatsResponse> GetMyStatsAsync(int userId, CancellationToken ct = default)
    {
        var games = await repository.GetMyGamesAsync(userId, ct);
        if (games.Count == 0)
            return new MyStatsResponse(0, 0, 0, 0, 0, 0, null, null);

        var wins = games.Count(g => g.Placement == 1);
        var answered = games.Sum(g => (long)g.AnsweredRounds);
        var correct = games.Sum(g => (long)g.CorrectAnswers);
        var seconds = games.Sum(g => (long)(g.EndTime - g.StartTime).TotalSeconds);

        // Tie: the category with the newest game wins.
        var category = games
            .GroupBy(g => g.CatId)
            .Select(grp =>
            {
                var newest = grp.MaxBy(g => g.EndTime)!;
                return (Dto: new MostPlayedCategoryDto(grp.Key, newest.CategoryName, grp.Count()), Last: newest.EndTime);
            })
            .OrderByDescending(x => x.Dto.Games).ThenByDescending(x => x.Last)
            .First().Dto;

        return new MyStatsResponse(
            games.Count,
            wins,
            (double)wins / games.Count,
            answered == 0 ? 0 : (double)correct / answered,
            games.Sum(g => g.Rounds),
            seconds,
            category,
            await FindMostCommonFriendAsync(userId, games, ct));
    }

    private async Task<MostCommonFriendDto?> FindMostCommonFriendAsync(
        int userId, IReadOnlyList<MyGameRow> myGames, CancellationToken ct)
    {
        var endTimes = myGames.ToDictionary(g => g.GameId, g => g.EndTime);
        var others = await repository.GetCoPlayersAsync(userId, ct);

        // Tie: the friend from the newest shared game wins. The name comes from that game.
        return others
            .Where(o => o.UserId != userId && endTimes.ContainsKey(o.GameId))
            .GroupBy(o => o.UserId)
            .Select(grp =>
            {
                var newest = grp.MaxBy(o => endTimes[o.GameId])!;
                return (Dto: new MostCommonFriendDto(grp.Key, newest.UserName, grp.Select(o => o.GameId).Distinct().Count()),
                        Last: endTimes[newest.GameId]);
            })
            .OrderByDescending(x => x.Dto.GamesTogether).ThenByDescending(x => x.Last)
            .Select(x => x.Dto)
            .FirstOrDefault();
    }
}
