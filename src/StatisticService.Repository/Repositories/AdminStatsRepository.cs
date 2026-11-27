using Microsoft.EntityFrameworkCore;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Repositories;

public class AdminStatsRepository(AppDbContext context) : IAdminStatsRepository
{
    // One DELETE ... WHERE "UserName" LIKE 'loadbot\_%' in PostgreSQL.
    public Task<int> DeleteUserStatsByUserNamePrefixAsync(string prefix, CancellationToken ct) =>
        context.UserStats.Where(s => s.UserName.StartsWith(prefix)).ExecuteDeleteAsync(ct);
}
