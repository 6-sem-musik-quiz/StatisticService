using StatisticService.Repository.Interfaces;
using StatisticService.Service.Interfaces;

namespace StatisticService.Service.Services;

public class AdminStatsService(IAdminStatsRepository repository) : IAdminStatsService
{
    // The k6 bots are named loadbot_N (gitops/loadtest, Platform ADR-0005).
    public const string LoadBotPrefix = "loadbot_";

    public Task<int> DeleteLoadBotStatsAsync(string prefix, CancellationToken ct)
    {
        if (!string.Equals(prefix, LoadBotPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"Only the load-test prefix '{LoadBotPrefix}' can be deleted.", nameof(prefix));
        return repository.DeleteUserStatsByUserNamePrefixAsync(LoadBotPrefix, ct);
    }
}
