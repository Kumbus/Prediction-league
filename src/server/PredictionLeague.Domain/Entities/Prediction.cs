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

    // Optional granular guess, scored only if the league's rules award it. A player reference
    // rather than a name so scoring compares against MatchEvent.PlayerId by equality (FR-005).
    public Guid? PredictedFirstScorerPlayerId { get; set; }

    // The team the goal is credited to — the other half of the scorer forecast. Predicting a
    // player from the *opposing* team here is how an own goal is expressed, which is the same
    // shape MatchEvent records (PlayerId alongside TeamId).
    public Guid? PredictedFirstScorerTeamId { get; set; }

    // Card-count guesses, one per card ScoringParameter a league can select. Null when the
    // league does not score that parameter.
    public int? PredictedTotalCards { get; set; }

    public int? PredictedYellowCards { get; set; }

    public int? PredictedRedCards { get; set; }

    public DateTimeOffset SubmittedUtc { get; set; }

    // Points awarded after the match is scored (FR-011); null until then.
    public int? AwardedPoints { get; set; }
}
