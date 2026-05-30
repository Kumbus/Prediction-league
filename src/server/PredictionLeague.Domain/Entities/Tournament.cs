namespace PredictionLeague.Domain.Entities;

// Admin-seeded competition (FR-003); fixtures/results ingested from a data API (FR-004).
public class Tournament
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // Identifier of this tournament in the external football data API.
    public string? ExternalApiId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public ICollection<Match> Matches { get; set; } = [];
}
