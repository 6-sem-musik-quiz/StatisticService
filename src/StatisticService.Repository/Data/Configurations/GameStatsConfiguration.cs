using StatisticService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatisticService.Repository.Data.Configurations;

public class GameStatsConfiguration : IEntityTypeConfiguration<GameStats>
{
    public void Configure(EntityTypeBuilder<GameStats> builder)
    {
        builder.HasKey(g => g.Id);
        // The id comes from GameService, not from the database.
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.CategoryName).IsRequired().HasMaxLength(60);

        // Weekly leaderboards filter on when the game ended.
        builder.HasIndex(g => g.EndTime);
    }
}
