using StatisticService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatisticService.Repository.Data.Configurations;

public class UserStatsConfiguration : IEntityTypeConfiguration<UserStats>
{
    public void Configure(EntityTypeBuilder<UserStats> builder)
    {
        builder.HasKey(u => new { u.GameId, u.UserId });
        builder.Property(u => u.UserName).IsRequired().HasMaxLength(20);

        builder.HasOne(u => u.Game).WithMany(g => g.Players)
            .HasForeignKey(u => u.GameId).OnDelete(DeleteBehavior.Cascade);

        // "My stats" looks up all games for one user.
        builder.HasIndex(u => u.UserId);
    }
}
