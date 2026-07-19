using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);

        builder.Property(p => p.Position).HasConversion<int>();

        // External-id upsert key. Filtered on <> 0 so multiple players without an external id
        // (stored as 0) coexist — otherwise a seed CSV lacking ExternalPlayerId can't import.
        builder.HasIndex(p => p.ExternalPlayerId)
            .IsUnique()
            .HasFilter("[ExternalPlayerId] <> 0");

        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(p => p.ClubTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(p => p.NationalTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nationality>()
            .WithMany()
            .HasForeignKey(p => p.NationalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
