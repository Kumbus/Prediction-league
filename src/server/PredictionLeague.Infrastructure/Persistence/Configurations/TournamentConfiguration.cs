using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
{
    public void Configure(EntityTypeBuilder<Tournament> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.ExternalApiId).HasMaxLength(100);

        // Ingest's GetByExternalApiIdAsync lookup must be deterministic; allow many NULLs but
        // forbid duplicate non-null values.
        builder.HasIndex(t => t.ExternalApiId)
            .IsUnique()
            .HasFilter("[ExternalApiId] IS NOT NULL");

        builder.HasMany(t => t.Matches)
            .WithOne()
            .HasForeignKey(m => m.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
