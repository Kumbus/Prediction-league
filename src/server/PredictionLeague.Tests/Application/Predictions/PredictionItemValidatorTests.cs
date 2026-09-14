using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Predictions;
using PredictionLeague.Domain.Entities;
using Shouldly;

namespace PredictionLeague.Tests.Application.Predictions;

// What counts as a forecast the league can store. The rule this file exists for is the one that
// was wrong in production: a submitted item with no score was written as a real 0–0 and reported
// as saved, because the request bound its scores into non-nullable ints and an absent field is
// indistinguishable from a zero once it has been through that. The member found out from a
// standings table that had scored a forecast they never made.
//
// So the two cases that matter most sit next to each other here and must never converge: an
// ABSENT score is refused, an EXPLICIT 0–0 is a real forecast and is accepted.
public class PredictionItemValidatorTests
{
    private static readonly Guid MatchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HomeTeamId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AwayTeamId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid HomePlayerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AwayPlayerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid StrangerId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static MatchRoundDto Match() => new(
        MatchId,
        "R1",
        DateTimeOffset.UtcNow.AddDays(1),
        MatchStatus.Scheduled,
        new TeamRefDto(HomeTeamId, "Home", null),
        new TeamRefDto(AwayTeamId, "Away", null));

    private static IReadOnlySet<ScoringParameter> Scores(params ScoringParameter[] parameters)
        => parameters.ToHashSet();

    private static IReadOnlyList<EligibleScorerDto> Squads() =>
    [
        new(HomePlayerId, "Home Striker", HomeTeamId),
        new(AwayPlayerId, "Away Striker", AwayTeamId),
    ];

    private static PredictionItemInput Item(
        int? homeScore = 2,
        int? awayScore = 1,
        Guid? firstScorerPlayerId = null,
        Guid? firstScorerTeamId = null,
        int? totalCards = null,
        int? yellowCards = null,
        int? redCards = null)
        => new(MatchId, homeScore, awayScore, firstScorerPlayerId, firstScorerTeamId,
            totalCards, yellowCards, redCards);

    private static PredictionItemValidation Validate(
        PredictionItemInput item,
        IReadOnlySet<ScoringParameter>? scored = null)
        => PredictionItemValidator.Validate(
            item,
            Match(),
            scored ?? Scores(ScoringParameter.ExactScore),
            Squads());

    // ---------------------------------------------------------------------------------------
    // Scores: absent is not zero
    // ---------------------------------------------------------------------------------------

    // The regression. A null score reached the old code as 0, passed the range check, and was
    // stored. It has to be refused before anything else looks at it.
    [Fact]
    public void Validate_BothScoresAbsent_IsRejected()
    {
        var result = Validate(Item(homeScore: null, awayScore: null));

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe("Both scores are required.");
    }

    // Half a scoreline is not a forecast either, and each half is checked — a rule that only
    // looked at the home score would let the away one through.
    [Fact]
    public void Validate_HomeScoreAbsent_IsRejected()
    {
        Validate(Item(homeScore: null)).Error.ShouldBe("Both scores are required.");
    }

    [Fact]
    public void Validate_AwayScoreAbsent_IsRejected()
    {
        Validate(Item(awayScore: null)).Error.ShouldBe("Both scores are required.");
    }

    // The case the fix must not break, and the reason "absent" and "zero" had to become
    // distinguishable rather than "0 is now invalid": a goalless draw is an ordinary forecast.
    [Fact]
    public void Validate_ExplicitNilNil_IsAccepted()
    {
        var result = Validate(Item(homeScore: 0, awayScore: 0));

        result.IsValid.ShouldBeTrue();
        result.HomeScore.ShouldBe(0);
        result.AwayScore.ShouldBe(0);
    }

    // The accepted scores travel with the verdict, so the caller never re-reads the nullables the
    // validator already proved present.
    [Fact]
    public void Validate_AValidForecast_CarriesItsScores()
    {
        var result = Validate(Item(homeScore: 3, awayScore: 1));

        result.IsValid.ShouldBeTrue();
        result.HomeScore.ShouldBe(3);
        result.AwayScore.ShouldBe(1);
    }

    // A missing score and an out-of-range score are different mistakes and say so differently:
    // telling a member their absent score is "between 0 and 99" would not help them fix it.
    [Fact]
    public void Validate_ScoreAboveTheCap_ReportsTheRangeRatherThanAbsence()
    {
        var result = Validate(Item(homeScore: PredictionItemValidator.MaxScore + 1));

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe($"Scores must be between 0 and {PredictionItemValidator.MaxScore}.");
    }

    [Fact]
    public void Validate_NegativeScore_IsRejected()
    {
        Validate(Item(awayScore: -1)).IsValid.ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------------------
    // Fields the league does not score are refused, not dropped
    // ---------------------------------------------------------------------------------------

    // Dropping it silently would let a member believe they forecast something that can never pay.
    [Fact]
    public void Validate_CardsSentToALeagueThatDoesNotScoreThem_IsRejected()
    {
        var result = Validate(Item(yellowCards: 3));

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe("This league does not score yellow cards.");
    }

    [Fact]
    public void Validate_AScorerSentToALeagueThatDoesNotScoreOne_IsRejected()
    {
        var result = Validate(Item(firstScorerPlayerId: HomePlayerId, firstScorerTeamId: HomeTeamId));

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe("This league does not score the first goal scorer.");
    }

    // Scored by the league and left blank: optional, and forfeits only those points.
    [Fact]
    public void Validate_AScoredCardFieldLeftBlank_IsAccepted()
    {
        var result = Validate(
            Item(yellowCards: null),
            Scores(ScoringParameter.ExactScore, ScoringParameter.CorrectYellowCards));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AScoredCardFieldAboveTheCap_IsRejected()
    {
        var result = Validate(
            Item(yellowCards: PredictionItemValidator.MaxCards + 1),
            Scores(ScoringParameter.ExactScore, ScoringParameter.CorrectYellowCards));

        result.Error.ShouldBe($"Yellow cards must be between 0 and {PredictionItemValidator.MaxCards}.");
    }

    // ---------------------------------------------------------------------------------------
    // First scorer: a pair, or nothing
    // ---------------------------------------------------------------------------------------

    private static IReadOnlySet<ScoringParameter> WithScorer()
        => Scores(ScoringParameter.ExactScore, ScoringParameter.CorrectGoalScorer);

    // Blank is allowed even where the league scores it — otherwise a league whose teams have no
    // linked players could never be forecast at all.
    [Fact]
    public void Validate_NoScorerInALeagueThatScoresOne_IsAccepted()
    {
        Validate(Item(), WithScorer()).IsValid.ShouldBeTrue();
    }

    // A player with no credited team cannot be scored, and a team with no player says nothing.
    [Fact]
    public void Validate_AScorerWithoutItsCreditedTeam_IsRejected()
    {
        var result = Validate(Item(firstScorerPlayerId: HomePlayerId), WithScorer());

        result.Error.ShouldBe("Pick both a first scorer and the team the goal is credited to.");
    }

    [Fact]
    public void Validate_ACreditedTeamWithoutAScorer_IsRejected()
    {
        var result = Validate(Item(firstScorerTeamId: HomeTeamId), WithScorer());

        result.Error.ShouldBe("Pick both a first scorer and the team the goal is credited to.");
    }

    [Fact]
    public void Validate_ACreditedTeamNotPlayingThisMatch_IsRejected()
    {
        var result = Validate(
            Item(firstScorerPlayerId: HomePlayerId, firstScorerTeamId: StrangerId),
            WithScorer());

        result.Error.ShouldBe("The credited team must be one of the two teams playing.");
    }

    [Fact]
    public void Validate_APlayerInNeitherSquad_IsRejected()
    {
        var result = Validate(
            Item(firstScorerPlayerId: StrangerId, firstScorerTeamId: HomeTeamId),
            WithScorer());

        result.Error.ShouldBe("That player is not in either team's squad for this match.");
    }

    // The own-goal forecast: the player and the credited team are deliberately NOT required to
    // agree, because that is exactly the shape MatchEvent records. A validator that "fixed" this
    // mismatch would make own goals unforecastable.
    [Fact]
    public void Validate_APlayerCreditedToTheOpposingTeam_IsAcceptedAsAnOwnGoal()
    {
        var result = Validate(
            Item(firstScorerPlayerId: HomePlayerId, firstScorerTeamId: AwayTeamId),
            WithScorer());

        result.IsValid.ShouldBeTrue();
    }
}
