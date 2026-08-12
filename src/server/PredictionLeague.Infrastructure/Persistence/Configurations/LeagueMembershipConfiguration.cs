using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class LeagueMembershipConfiguration : IEntityTypeConfiguration<LeagueMembership>
{
    public void Configure(EntityTypeBuilder<LeagueMembership> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<int>();

        // The default is what puts a real timestamp on rows that predate the column — without it
        // the migration scaffolds defaultValue: 0001-01-01 for a required column and backdates
        // every existing membership to year 1. Inserts set the value explicitly anyway, so join
        // time never depends on the database clock.
        builder.Property(m => m.JoinedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

        // One membership per (user, league).
        builder.HasIndex(m => new { m.LeagueId, m.UserId }).IsUnique();

        // UserId is a bare Guid → ApplicationUser.Id; no FK constraint this slice.
    }
}
