using PredictionLeague.Domain.Entities;
using PredictionLeague.Domain.Scoring;
using Shouldly;
using static PredictionLeague.Tests.Domain.Scoring.ScoringFixtures;

namespace PredictionLeague.Tests.Domain.Scoring;

// Risk #1: a member's awarded total equals the number derived from *their league's* rule
// configuration. Every expected value below is stated as a literal traceable to the rule list the
// test itself configures — never computed by summing that list, which would mirror the engine's
// own loop and pass against a broken one.
public class PredictionScorerTests
{
    // ---------------------------------------------------------------------------------------
    // A league that configured nothing
    // ---------------------------------------------------------------------------------------

    // A league with no scoring rules is a legitimate state, not an error (MatchScoringService
    // hands such a league a static empty rule list). Every member scores 0 no matter how good the
    // forecast was: there is nothing configured to pay out.
    [Fact]
    public void Score_LeagueWithNoConfiguredRules_AwardsZero()
    {
        var total = PredictionScorer.Score(
            Forecast(2, 1, PlayerX, TeamA, totalCards: 3, yellowCards: 2, redCards: 1),
            Result(2, 1, PlayerX, TeamA, totalCards: 3, yellowCards: 2, redCards: 1),
            Rules());

        total.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------------------
    // ExactScore
    // ---------------------------------------------------------------------------------------

    // League "Scoreline Cup" configures ExactScore = 3 and nothing else, so the only two totals
    // reachable here are 3 (both scores right) and 0. Getting the *result* right is not enough —
    // that is what CorrectOutcome is for, and this league does not configure it.
    [Theory]
    // predicted home/away, actual home/away, expected
    [InlineData(2, 1, 2, 1, 3)] // exactly right
    [InlineData(0, 0, 0, 0, 3)] // a goalless draw is an exact score like any other
    [InlineData(2, 1, 3, 0, 0)] // right outcome (home win), wrong scoreline
    [InlineData(1, 1, 2, 2, 0)] // right outcome (draw), wrong scoreline
    [InlineData(2, 1, 2, 0, 0)] // home score right, away score wrong
    [InlineData(2, 1, 1, 1, 0)] // away score right, home score wrong
    [InlineData(2, 1, 1, 2, 0)] // the mirrored scoreline is a different result entirely
    public void Score_ExactScore_AwardsOnlyWhenBothScoresMatch(
        int predictedHome, int predictedAway, int actualHome, int actualAway, int expected)
    {
        var total = PredictionScorer.Score(
            Forecast(predictedHome, predictedAway),
            Result(actualHome, actualAway),
            Rules((ScoringParameter.ExactScore, 3)));

        total.ShouldBe(expected);
    }

    // ---------------------------------------------------------------------------------------
    // CorrectOutcome
    // ---------------------------------------------------------------------------------------

    // League "Results Only" configures CorrectOutcome = 2. The rule is about the *result* —
    // home win, draw, away win — so the scoreline may be wildly wrong and still pay out. All
    // three result classes are covered in both the awarding and the non-awarding direction.
    [Theory]
    // predicted home/away, actual home/away, expected
    [InlineData(3, 1, 1, 0, 2)] // home win predicted, home win happened
    [InlineData(1, 1, 0, 0, 2)] // draw predicted, draw happened
    [InlineData(0, 2, 1, 4, 2)] // away win predicted, away win happened
    [InlineData(2, 0, 0, 1, 0)] // home win predicted, away win happened
    [InlineData(2, 0, 1, 1, 0)] // home win predicted, draw happened
    [InlineData(1, 1, 2, 1, 0)] // draw predicted, home win happened
    [InlineData(1, 1, 0, 3, 0)] // draw predicted, away win happened
    [InlineData(0, 3, 2, 2, 0)] // away win predicted, draw happened
    [InlineData(0, 3, 2, 1, 0)] // away win predicted, home win happened
    public void Score_CorrectOutcome_AwardsOnlyWhenTheResultClassMatches(
        int predictedHome, int predictedAway, int actualHome, int actualAway, int expected)
    {
        var total = PredictionScorer.Score(
            Forecast(predictedHome, predictedAway),
            Result(actualHome, actualAway),
            Rules((ScoringParameter.CorrectOutcome, 2)));

        total.ShouldBe(expected);
    }

    // ---------------------------------------------------------------------------------------
    // CorrectGoalScorer
    // ---------------------------------------------------------------------------------------

    // League "First Blood" configures CorrectGoalScorer = 4. The forecast is a *pair* — which
    // player, credited to which team — and both halves must match. Half a forecast, or half a
    // match, awards nothing.
    public static TheoryData<Guid?, Guid?, Guid?, Guid?, int> GoalScorerCases() => new()
    {
        // predicted player, predicted team, actual player, actual team, expected
        { PlayerX, TeamA, PlayerX, TeamA, 4 }, // both halves match
        { PlayerX, TeamA, PlayerX, TeamB, 0 }, // right player, credited to the other team
        { PlayerX, TeamA, PlayerY, TeamA, 0 }, // right team, wrong player
        { PlayerX, TeamA, PlayerY, TeamB, 0 }, // neither half
        { null, TeamA, PlayerX, TeamA, 0 },    // blank player half
        { PlayerX, null, PlayerX, TeamA, 0 },  // blank team half
        { null, null, PlayerX, TeamA, 0 },     // no forecast at all
        { PlayerX, TeamA, null, null, 0 }      // no qualifying goal was recorded in the match
    };

    [Theory]
    [MemberData(nameof(GoalScorerCases))]
    public void Score_CorrectGoalScorer_AwardsOnlyWhenPlayerAndCreditedTeamBothMatch(
        Guid? predictedPlayer, Guid? predictedTeam, Guid? actualPlayer, Guid? actualTeam, int expected)
    {
        var total = PredictionScorer.Score(
            Forecast(1, 0, predictedPlayer, predictedTeam),
            Result(1, 0, actualPlayer, actualTeam),
            Rules((ScoringParameter.CorrectGoalScorer, 4)));

        total.ShouldBe(expected);
    }

    // An own goal needs no special case in the engine: it is already expressed by the pair. PlayerX
    // belongs to team A, but the goal they put past their own keeper is credited to team B — and
    // that is the pair a member has to name to collect.
    [Fact]
    public void Score_CorrectGoalScorer_OwnGoal_AwardsWhenTheForecastCreditsTheBenefitingTeam()
    {
        var total = PredictionScorer.Score(
            Forecast(0, 1, PlayerX, TeamB),
            Result(0, 1, PlayerX, TeamB),
            Rules((ScoringParameter.CorrectGoalScorer, 4)));

        total.ShouldBe(4);
    }

    // Naming that same player alongside their own team is a different forecast, and a wrong one.
    [Fact]
    public void Score_CorrectGoalScorer_OwnGoal_AwardsNothingWhenTheForecastCreditsTheScorersOwnTeam()
    {
        var total = PredictionScorer.Score(
            Forecast(0, 1, PlayerX, TeamA),
            Result(0, 1, PlayerX, TeamB),
            Rules((ScoringParameter.CorrectGoalScorer, 4)));

        total.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------------------
    // Card counts
    // ---------------------------------------------------------------------------------------

    // League "Discipline" configures CorrectCardCount = 2. Exact equality against the match total:
    // one card out in either direction awards nothing, so neither a >= comparison nor a tolerance
    // band survives. The last case is cross-wired — the forecast's total is wrong but its yellow
    // count is right — which fails if the rule ever reads the wrong field.
    [Theory]
    // predicted total/yellow/red, actual total/yellow/red, expected
    [InlineData(5, null, null, 5, 3, 2, 2)]    // exactly right
    [InlineData(0, null, null, 0, 0, 0, 2)]    // zero is a correct answer, not an absent one
    [InlineData(4, null, null, 5, 3, 2, 0)]    // one under
    [InlineData(6, null, null, 5, 3, 2, 0)]    // one over
    [InlineData(null, null, null, 5, 3, 2, 0)] // blank forecast awards nothing, never throws
    [InlineData(3, 3, 0, 5, 3, 2, 0)]          // cross-wired: matches the yellow count, not the total
    public void Score_CorrectCardCount_AwardsOnlyOnExactEqualityWithTheMatchTotal(
        int? predictedTotal, int? predictedYellow, int? predictedRed,
        int actualTotal, int actualYellow, int actualRed, int expected)
    {
        var total = PredictionScorer.Score(
            Forecast(1, 0, totalCards: predictedTotal, yellowCards: predictedYellow, redCards: predictedRed),
            Result(1, 0, totalCards: actualTotal, yellowCards: actualYellow, redCards: actualRed),
            Rules((ScoringParameter.CorrectCardCount, 2)));

        total.ShouldBe(expected);
    }

    // League "Bookings" configures CorrectYellowCards = 1. The cross-wired case gets the total and
    // the red count right and the yellow count wrong — this league pays for neither of the first two.
    [Theory]
    // predicted total/yellow/red, actual total/yellow/red, expected
    [InlineData(null, 3, null, 5, 3, 2, 1)]    // exactly right
    [InlineData(null, 0, null, 0, 0, 0, 1)]    // zero is a correct answer
    [InlineData(null, 2, null, 5, 3, 2, 0)]    // one under
    [InlineData(null, 4, null, 5, 3, 2, 0)]    // one over
    [InlineData(null, null, null, 5, 3, 2, 0)] // blank forecast
    [InlineData(5, 5, 2, 5, 3, 2, 0)]          // cross-wired: total and red right, yellow wrong
    public void Score_CorrectYellowCards_AwardsOnlyOnExactEqualityWithTheYellowCount(
        int? predictedTotal, int? predictedYellow, int? predictedRed,
        int actualTotal, int actualYellow, int actualRed, int expected)
    {
        var total = PredictionScorer.Score(
            Forecast(1, 0, totalCards: predictedTotal, yellowCards: predictedYellow, redCards: predictedRed),
            Result(1, 0, totalCards: actualTotal, yellowCards: actualYellow, redCards: actualRed),
            Rules((ScoringParameter.CorrectYellowCards, 1)));

        total.ShouldBe(expected);
    }

    // League "Sendings Off" configures CorrectRedCards = 3. The cross-wired case gets the total and
    // the yellow count right and the red count wrong.
    [Theory]
    // predicted total/yellow/red, actual total/yellow/red, expected
    [InlineData(null, null, 2, 5, 3, 2, 3)]    // exactly right
    [InlineData(null, null, 0, 0, 0, 0, 3)]    // zero is a correct answer — most matches have no red
    [InlineData(null, null, 1, 5, 3, 2, 0)]    // one under
    [InlineData(null, null, 3, 5, 3, 2, 0)]    // one over
    [InlineData(null, null, null, 5, 3, 2, 0)] // blank forecast
    [InlineData(5, 3, 3, 5, 3, 2, 0)]          // cross-wired: total and yellow right, red wrong
    public void Score_CorrectRedCards_AwardsOnlyOnExactEqualityWithTheRedCount(
        int? predictedTotal, int? predictedYellow, int? predictedRed,
        int actualTotal, int actualYellow, int actualRed, int expected)
    {
        var total = PredictionScorer.Score(
            Forecast(1, 0, totalCards: predictedTotal, yellowCards: predictedYellow, redCards: predictedRed),
            Result(1, 0, totalCards: actualTotal, yellowCards: actualYellow, redCards: actualRed),
            Rules((ScoringParameter.CorrectRedCards, 3)));

        total.ShouldBe(expected);
    }

    // ---------------------------------------------------------------------------------------
    // Rules stack
    // ---------------------------------------------------------------------------------------

    // League "Classic" configures ExactScore = 5 and CorrectOutcome = 2. A member who nails the
    // scoreline has also, necessarily, named the right result — and collects for both, because each
    // configured rule means exactly what the organizer's editor said it means. 5 + 2 = 7.
    [Fact]
    public void Score_ExactScoreAndCorrectOutcome_AwardBothWhenTheScorelineIsPerfect()
    {
        var total = PredictionScorer.Score(
            Forecast(3, 1),
            Result(3, 1),
            Rules((ScoringParameter.ExactScore, 5), (ScoringParameter.CorrectOutcome, 2)));

        total.ShouldBe(7);
    }

    // The same league, same rules, a member who read the result but not the scoreline: 2 and only 2.
    // ExactScore does not bundle CorrectOutcome, and it does not supersede it either.
    [Fact]
    public void Score_ExactScoreAndCorrectOutcome_AwardOnlyTheOutcomeWhenTheScorelineIsWrong()
    {
        var total = PredictionScorer.Score(
            Forecast(3, 1),
            Result(1, 0),
            Rules((ScoringParameter.ExactScore, 5), (ScoringParameter.CorrectOutcome, 2)));

        total.ShouldBe(2);
    }

    // A parameter the league did not configure contributes nothing, however right the forecast was.
    // This member named the first scorer and all three card counts exactly, in a league that pays
    // for none of them: 5 for the exact score, and not a point more.
    [Fact]
    public void Score_UnconfiguredParameters_ContributeNothingHoweverRightTheForecast()
    {
        var total = PredictionScorer.Score(
            Forecast(3, 1, PlayerX, TeamA, totalCards: 4, yellowCards: 3, redCards: 1),
            Result(3, 1, PlayerX, TeamA, totalCards: 4, yellowCards: 3, redCards: 1),
            Rules((ScoringParameter.ExactScore, 5)));

        total.ShouldBe(5);
    }

    // ---------------------------------------------------------------------------------------
    // Argument guards
    // ---------------------------------------------------------------------------------------

    // Score is public Domain API and its null guards are part of the contract: a missing forecast,
    // a missing outcome or a missing rule list is a caller bug, not a zero-point member.
    [Fact]
    public void Score_NullPrediction_Throws()
        => Should.Throw<ArgumentNullException>(
                () => PredictionScorer.Score(null!, Result(1, 0), Rules()))
            .ParamName.ShouldBe("prediction");

    [Fact]
    public void Score_NullOutcome_Throws()
        => Should.Throw<ArgumentNullException>(
                () => PredictionScorer.Score(Forecast(1, 0), null!, Rules()))
            .ParamName.ShouldBe("outcome");

    [Fact]
    public void Score_NullRules_Throws()
        => Should.Throw<ArgumentNullException>(
                () => PredictionScorer.Score(Forecast(1, 0), Result(1, 0), null!))
            .ParamName.ShouldBe("rules");
}
