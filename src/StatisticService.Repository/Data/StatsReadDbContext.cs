using Microsoft.EntityFrameworkCore;

namespace StatisticService.Repository.Data;

// SCRUM-202: the read replicas (connection string "Read", the service quizdb-postgresql-read).
// Same model as AppDbContext, but it can only read: queries are not tracked, and saving throws,
// because a replica refuses every write ("cannot execute ... in a read-only transaction").
// A replica can be a few milliseconds behind the primary, so only reads that do not need a
// write from just before go here: the leaderboard fallback and "my stats". The leaderboard
// rebuild reads from the primary (see WeeklyStatsRepository).
public sealed class StatsReadDbContext(DbContextOptions<StatsReadDbContext> options) : AppDbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    private static InvalidOperationException ReadOnly() =>
        new("StatsReadDbContext reads from the read replicas and cannot save. Write through AppDbContext (the primary).");
}
