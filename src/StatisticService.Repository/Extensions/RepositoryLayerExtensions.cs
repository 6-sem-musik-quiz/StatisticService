using StatisticService.Repository.Cache;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;
using StatisticService.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace StatisticService.Repository.Extensions;

public static class RepositoryLayerExtensions
{
    // connectionString: the primary (reads and writes, migrations).
    // readConnectionString (SCRUM-202): the read replicas. null or empty = read from the primary too
    // (local development and tests have one Postgres).
    // redisConnectionString (SCRUM-195): the leaderboard cache. null or empty = no cache, the
    // leaderboard is read from Postgres only.
    public static IServiceCollection AddRepositoryLayer(this IServiceCollection services, string connectionString,
        string? redisConnectionString = null, string? readConnectionString = null)
    {
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        var replicas = string.IsNullOrWhiteSpace(readConnectionString) ? connectionString : readConnectionString;
        services.AddDbContext<StatsReadDbContext>(options => options.UseNpgsql(replicas));
        services.AddScoped<IWeeklyStatsRepository, WeeklyStatsRepository>();
        services.AddScoped<IMyStatsRepository, MyStatsRepository>();
        services.AddScoped<IGameResultRepository, GameResultRepository>();
        services.AddScoped<IAdminStatsRepository, AdminStatsRepository>(); // SCRUM-197

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<ILeaderboardCache, NoLeaderboardCache>();
        }
        else
        {
            // Resolved lazily and without AbortOnConnectFail, so the service (and /health) starts
            // even when Redis is down; the leaderboard then falls back to Postgres. Short timeouts,
            // so a dead Redis costs a request at most a couple of seconds before the fallback.
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redisConnectionString);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = 2000;
                options.AsyncTimeout = 2000;
                options.SyncTimeout = 2000;
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<ILeaderboardCache, RedisLeaderboardCache>();
        }

        return services;
    }

    // Applies the migrations that are not in the database yet. Each pod runs this on
    // startup, so every image Flux deploys from main brings its schema changes with it.
    // It is safe to run on every start and from several pods at once:
    // - EF records applied migrations in "__EFMigrationsHistory" and skips those.
    // - EF Core 9+ locks that table while migrating, so a second pod waits and then
    //   finds nothing left to apply.
    public static void MigrateDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The EF InMemory provider used in tests has no migrations.
        if (db.Database.IsRelational())
            db.Database.Migrate();
    }
}
