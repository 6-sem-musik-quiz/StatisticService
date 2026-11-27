using StatisticService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace StatisticService.Repository.Data;

// The primary (SCRUM-202): reads and writes, and the only context migrations run against.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // For StatsReadDbContext, which needs options of its own type.
    protected AppDbContext(DbContextOptions options) : base(options) { }

    public DbSet<GameStats> GameStats => Set<GameStats>();
    public DbSet<UserStats> UserStats => Set<UserStats>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> in this assembly (see Data/Configurations).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
