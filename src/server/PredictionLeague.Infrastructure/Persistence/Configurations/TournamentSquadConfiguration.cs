using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class TournamentSquadConfiguration : IEntityTypeConfiguration<TournamentSquad>
{
    public void Configure(EntityTypeBuilder<TournamentSquad> builder)
    {
        builder.HasKey(ts => new { ts.TournamentId, ts.PlayerId });

        builder.HasOne<Tournament>()
            .WithMany()
            .HasForeignKey(ts => ts.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(ts => ts.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
