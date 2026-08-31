using PredictionLeague.Domain.Entities;
using PredictionLeague.Domain.Scoring;
using Shouldly;

namespace PredictionLeague.Tests.Domain.Scoring;

public class PredictionScorerTests
{
    // A league that configured no scoring rules is a legitimate state, not an error
    // (MatchScoringService hands such a league a static empty rule list). Every member of that
    // league scores 0 no matter how good the forecast was — there is nothing configured to pay out.
    [Fact]
    public void Score_LeagueWithNoConfiguredRules_AwardsZero()
    {
        var prediction = new Prediction
        {
            PredictedHomeScore = 2,
            PredictedAwayScore = 1
        };

        var outcome = new MatchOutcome(
            HomeScore: 2,
            AwayScore: 1,
            FirstScorerPlayerId: null,
            FirstScorerTeamId: null,
            TotalCards: 0,
            YellowCards: 0,
            RedCards: 0);

        var total = PredictionScorer.Score(prediction, outcome, Array.Empty<ScoringRule>());

        total.ShouldBe(0);
    }
}
