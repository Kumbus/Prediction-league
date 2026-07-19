namespace PredictionLeague.Domain.Entities;

// Per-tournament roster row. Optional: CSV import may write here; ingest does not depend on it.
public class TournamentSquad
{
    public Guid TournamentId { get; set; }

    public Guid PlayerId { get; set; }
}
