namespace PredictionLeague.Domain.Entities;

// Admin-seeded competition (FR-003); fixtures/results ingested from a data API (FR-004).
public class Tournament
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // Identifier of this tournament in the external football data API.
    public string? ExternalApiId { get; set; }

    // API league.season — passed to ingest by the production timer (no query param).
    public int Season { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    // Visibility gate (FR-003). Drafts hidden from non-admin reads; ingest still works on Drafts.
    public bool IsPublished { get; set; }

    public ICollection<Match> Matches { get; set; } = [];
}
