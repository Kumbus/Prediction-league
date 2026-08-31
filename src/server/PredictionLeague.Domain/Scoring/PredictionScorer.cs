using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Domain.Scoring;

// The rules of the game, as a pure function (FR-011). No database, no clock, no EF: everything
// downstream calls this and nothing else decides what a point is worth.
//
// Rules stack cumulatively — a member who nails the exact score in a league scoring both
// ExactScore and CorrectOutcome collects both, because each configured rule means exactly what the
// organizer's editor said it means. A parameter the league does not configure contributes nothing,
// and its Points value is never read. Every point value comes from ScoringRule.Points; there are
// no literal point values in this file.
public static class PredictionScorer
{
    public static int Score(
        Prediction prediction,
        MatchOutcome outcome,
        IEnumerable<ScoringRule> rules)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(rules);

        var total = 0;
        foreach (var rule in rules)
        {
            if (Awards(rule.Parameter, prediction, outcome))
                total += rule.Points;
        }

        return total;
    }

    // One arm per ScoringParameter. An unknown parameter awards nothing rather than throwing: the
    // enum is append-only, and a league configured on a newer build must not make an older one fail
    // to score at all.
    private static bool Awards(ScoringParameter parameter, Prediction prediction, MatchOutcome outcome)
        => parameter switch
        {
            // Predicted home and away both equal the actual.
            ScoringParameter.ExactScore =>
                prediction.PredictedHomeScore == outcome.HomeScore
                && prediction.PredictedAwayScore == outcome.AwayScore,

            // Home win / draw / away win, compared by sign — a predicted draw matches an actual draw.
            ScoringParameter.CorrectOutcome =>
                Math.Sign(prediction.PredictedHomeScore - prediction.PredictedAwayScore)
                == Math.Sign(outcome.HomeScore - outcome.AwayScore),

            // Player *and* credited team must both match, which is what makes an own goal score
            // with no special case (a player credited to the opposing team). A forecast missing
            // either half, or a match with no qualifying goal, awards nothing — null never equals
            // a value under lifted comparison, so both cases fall out of the same expression.
            ScoringParameter.CorrectGoalScorer =>
                prediction.PredictedFirstScorerPlayerId is not null
                && prediction.PredictedFirstScorerTeamId is not null
                && prediction.PredictedFirstScorerPlayerId == outcome.FirstScorerPlayerId
                && prediction.PredictedFirstScorerTeamId == outcome.FirstScorerTeamId,

            // Card counts: a blank forecast (null) awards nothing; a match with no card events
            // counts as zero, so a member who predicted 0 is correct.
            ScoringParameter.CorrectCardCount => prediction.PredictedTotalCards == outcome.TotalCards,
            ScoringParameter.CorrectYellowCards => prediction.PredictedYellowCards == outcome.YellowCards,
            ScoringParameter.CorrectRedCards => prediction.PredictedRedCards == outcome.RedCards,

            _ => false
        };
}
