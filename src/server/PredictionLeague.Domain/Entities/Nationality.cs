namespace PredictionLeague.Domain.Entities;

// Lookup of citizenships (FR-005). Distinct from Team — Team is who plays for a nation;
// Nationality is the citizenship the player holds. Pre-seeded with ISO 3166-1 alpha-3 codes.
public class Nationality
{
    public int Id { get; set; }

    // ISO 3166-1 alpha-3 (e.g. "POL", "GBR"). Unique.
    public required string Code { get; set; }

    public required string Name { get; set; }
}
