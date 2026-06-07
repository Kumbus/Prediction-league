using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Round).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Status).HasConversion<int>();

        // Idempotent upsert key — without this every poll duplicates the fixture.
        builder.HasIndex(m => m.ExternalFixtureId).IsUnique();

        // Restrict (not Cascade) on the team FKs to avoid multiple-cascade-path errors:
        // a team is referenced from both home and away.
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(m => m.HomeTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(m => m.AwayTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Events)
            .WithOne()
            .HasForeignKey(e => e.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
