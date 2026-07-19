using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Configurations;

public class NationalityConfiguration : IEntityTypeConfiguration<Nationality>
{
    public void Configure(EntityTypeBuilder<Nationality> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Code).IsRequired().HasMaxLength(3);
        builder.Property(n => n.Name).IsRequired().HasMaxLength(100);

        builder.HasIndex(n => n.Code).IsUnique();
    }
}
