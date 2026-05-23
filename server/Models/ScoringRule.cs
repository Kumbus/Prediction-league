namespace PredictionLeague.Models;

// One line of a league's custom scoring config: which parameter counts, worth how many points (FR-008).
public class ScoringRule
{
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public ScoringParameter Parameter { get; set; }

    public int Points { get; set; }
}
