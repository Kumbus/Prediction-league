namespace PredictionLeague.Domain.Entities;

// A member's forecast for one match in one league (FR-009). Keyed per (user, league, match).
public class Prediction
{
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public Guid UserId { get; set; }

    public Guid MatchId { get; set; }

    public int PredictedHomeScore { get; set; }

    public int PredictedAwayScore { get; set; }

    // Optional granular guess, scored only if the league's rules award it.
    public string? PredictedFirstScorer { get; set; }

    public DateTimeOffset SubmittedUtc { get; set; }

    // Points awarded after the match is scored (FR-011); null until then.
    public int? AwardedPoints { get; set; }
}
