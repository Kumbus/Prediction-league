using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class LeagueConfiguration : IEntityTypeConfiguration<League>
{
    public void Configure(EntityTypeBuilder<League> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);
        builder.Property(l => l.InviteCode).IsRequired().HasMaxLength(32);

        builder.HasIndex(l => l.InviteCode).IsUnique();

        // Guards read-then-write on the league row itself — today that is the organizer transfer,
        // whose two halves would otherwise drift apart under concurrent calls. Inserts are not
        // checked, so the invite-code retry (which re-saves a still-Added graph) is unaffected.
        builder.Property(l => l.RowVersion).IsRowVersion();

        // Owned children: deleting a league removes its scoring config and memberships.
        builder.HasMany(l => l.ScoringRules)
            .WithOne()
            .HasForeignKey(r => r.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Memberships)
            .WithOne()
            .HasForeignKey(m => m.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        // OrganizerUserId / TournamentId are bare Guids — no FK constraint this slice.
    }
}
