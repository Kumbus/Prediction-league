using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class MatchEventTypeConfiguration : IEntityTypeConfiguration<MatchEventType>
{
    public void Configure(EntityTypeBuilder<MatchEventType> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).IsRequired().HasMaxLength(50);
        builder.Property(t => t.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Category).HasConversion<int>();

        builder.HasIndex(t => t.Code).IsUnique();

        // Stable seeded ids — referenced by the ingest service's detail → Code mapping.
        builder.HasData(
            new MatchEventType { Id = 1, Code = "NormalGoal", DisplayName = "Normal Goal", Category = MatchEventCategory.Goal },
            new MatchEventType { Id = 2, Code = "OwnGoal", DisplayName = "Own Goal", Category = MatchEventCategory.Goal },
            new MatchEventType { Id = 3, Code = "Penalty", DisplayName = "Penalty", Category = MatchEventCategory.Goal },
            new MatchEventType { Id = 4, Code = "MissedPenalty", DisplayName = "Missed Penalty", Category = MatchEventCategory.Goal },
            new MatchEventType { Id = 5, Code = "YellowCard", DisplayName = "Yellow Card", Category = MatchEventCategory.Card },
            new MatchEventType { Id = 6, Code = "RedCard", DisplayName = "Red Card", Category = MatchEventCategory.Card });
    }
}
