namespace StatisticService.Repository.Interfaces;

// SCRUM-197: cleanup after a load test.
public interface IAdminStatsRepository
{
    // Deletes every UserStats row whose UserName starts with prefix. GameStats rows are kept.
    // Returns the number of rows deleted. The caller validates the prefix.
    Task<int> DeleteUserStatsByUserNamePrefixAsync(string prefix, CancellationToken ct);
}
