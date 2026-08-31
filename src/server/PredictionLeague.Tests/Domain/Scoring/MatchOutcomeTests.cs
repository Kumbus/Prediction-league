using PredictionLeague.Domain.Entities;
using PredictionLeague.Domain.Scoring;
using Shouldly;
using static PredictionLeague.Tests.Domain.Scoring.ScoringFixtures;

namespace PredictionLeague.Tests.Domain.Scoring;

// Risk #1, second half: the match facts the engine scores against must be derived correctly from
// the recorded event list. Two things here are load-bearing and invisible to anyone reading the
// ordering expression cold — the MissedPenalty exclusion, and the absence of MatchEvent.Id from
// the sort keys.
public class MatchOutcomeTests
{
    // ---------------------------------------------------------------------------------------
    // Which events count as a goal
    // ---------------------------------------------------------------------------------------

    // A missed penalty is a shot, not a goal — but it is seeded under Category = Goal, so a filter
    // that trusted the category alone would hand the first-scorer points to the player who missed.
    // Here the miss is the earliest Goal-category event in the match and must lose to the real goal
    // recorded ten minutes later.
    [Fact]
    public void FromMatch_MissedPenaltyBeforeARealGoal_ResolvesTheRealGoalScorer()
    {
        var match = FinishedMatch(1, 0,
            Event(MissedPenaltyTypeId, PlayerY, TeamB, minute: 10),
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 20));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBe(PlayerX);
        outcome.FirstScorerTeamId.ShouldBe(TeamA);
    }

    // A match whose only Goal-category event is a missed penalty has no first scorer at all. Both
    // halves of the pair go null together — never one without the other, because a member's
    // forecast is only ever compared as a pair.
    [Fact]
    public void FromMatch_OnlyAMissedPenalty_ResolvesNoFirstScorer()
    {
        var match = FinishedMatch(0, 0,
            Event(MissedPenaltyTypeId, PlayerY, TeamB, minute: 10));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBeNull();
        outcome.FirstScorerTeamId.ShouldBeNull();
    }

    // A goalless match reports the same empty pair.
    [Fact]
    public void FromMatch_NoEventsAtAll_ResolvesNoFirstScorer()
    {
        var outcome = MatchOutcome.FromMatch(FinishedMatch(0, 0), SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBeNull();
        outcome.FirstScorerTeamId.ShouldBeNull();
    }

    // Three of the four Goal-category types put the ball in the net and qualify; the fourth does
    // not. Runs each type as the match's only event, so nothing but the type decides the answer.
    [Theory]
    [InlineData(NormalGoalTypeId, true)]
    [InlineData(OwnGoalTypeId, true)]
    [InlineData(PenaltyTypeId, true)]
    [InlineData(MissedPenaltyTypeId, false)]
    public void FromMatch_GoalCategoryTypes_QualifyAsFirstScorerExceptAMissedPenalty(
        int matchEventTypeId, bool qualifies)
    {
        var match = FinishedMatch(1, 0,
            Event(matchEventTypeId, PlayerX, TeamA, minute: 30));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBe(qualifies ? PlayerX : (Guid?)null);
        outcome.FirstScorerTeamId.ShouldBe(qualifies ? TeamA : (Guid?)null);
    }

    // An own goal is credited to the team that benefits from it, and the derivation does not
    // second-guess that: MatchEvent.TeamId is authoritative and is never inverted on the way out.
    [Fact]
    public void FromMatch_OwnGoal_CreditsTheTeamTheEventNames()
    {
        var match = FinishedMatch(0, 1,
            Event(OwnGoalTypeId, PlayerX, TeamB, minute: 55));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBe(PlayerX);
        outcome.FirstScorerTeamId.ShouldBe(TeamB);
    }

    // Cards are not goals, however early they were shown.
    [Fact]
    public void FromMatch_CardsBeforeTheFirstGoal_DoNotResolveAsFirstScorer()
    {
        var match = FinishedMatch(1, 0,
            Event(YellowCardTypeId, PlayerY, TeamB, minute: 3),
            Event(RedCardTypeId, PlayerZ, TeamB, minute: 8),
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 66));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBe(PlayerX);
        outcome.FirstScorerTeamId.ShouldBe(TeamA);
    }

    // ---------------------------------------------------------------------------------------
    // Ordering: the two keys that carry football meaning
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void FromMatch_TwoGoals_ResolvesTheEarlierMinute()
    {
        var match = FinishedMatch(1, 1,
            Event(NormalGoalTypeId, PlayerY, TeamB, minute: 70),
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 20));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBe(PlayerX);
        outcome.FirstScorerTeamId.ShouldBe(TeamA);
    }

    // A null MinuteExtra means "no stoppage time", which happens before 90+1 — not after it, and
    // not at some undefined position. A goal on the 90th minute proper beats one in added time.
    [Fact]
    public void FromMatch_AbsentStoppageTime_SortsAheadOfAddedTime()
    {
        var match = FinishedMatch(1, 1,
            Event(NormalGoalTypeId, PlayerY, TeamB, minute: 90, minuteExtra: 1),
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 90, minuteExtra: null));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBe(PlayerX);
        outcome.FirstScorerTeamId.ShouldBe(TeamA);
    }

    // ---------------------------------------------------------------------------------------
    // Determinism and Id-independence
    // ---------------------------------------------------------------------------------------

    // The events a match is made of arrive in whatever order the query returned them. The same
    // recorded facts must name the same first scorer regardless — this asserts nothing about which
    // player wins anything, only that the answer does not depend on presentation order.
    [Fact]
    public void FromMatch_SameEventsInADifferentOrder_ResolveTheSameFirstScorer()
    {
        var events = new[]
        {
            Event(MissedPenaltyTypeId, PlayerZ, TeamB, minute: 5),
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 41),
            Event(YellowCardTypeId, PlayerY, TeamB, minute: 55),
            Event(PenaltyTypeId, PlayerY, TeamB, minute: 78)
        };

        var asRecorded = MatchOutcome.FromMatch(FinishedMatch(1, 1, events), SeededEventTypes());
        var reversed = MatchOutcome.FromMatch(
            FinishedMatch(1, 1, events.Reverse().ToArray()), SeededEventTypes());

        reversed.FirstScorerPlayerId.ShouldBe(asRecorded.FirstScorerPlayerId);
        reversed.FirstScorerTeamId.ShouldBe(asRecorded.FirstScorerTeamId);
    }

    // The most valuable assertion in this file. MatchEvent.Id is a fresh Guid on every replace-all
    // save, so if it ever entered the ordering, re-saving a match without changing a single
    // recorded fact could move CorrectGoalScorer points from one member to another. The rationale
    // is a comment in the implementation; this is the test that enforces it.
    //
    // Re-derives the outcome over many independent Id reshuffles. Against correct code the result
    // is invariant by construction, so this can never flake; against an Id-sensitive ordering the
    // odds of surviving 40 reshuffles are negligible.
    [Theory]
    [MemberData(nameof(IdIndependenceMatches))]
    public void FromMatch_FirstScorer_DoesNotDependOnEventIds(string scenario, MatchEvent[] events)
    {
        scenario.ShouldNotBeEmpty();

        var baseline = MatchOutcome.FromMatch(FinishedMatch(2, 1, events), SeededEventTypes());

        for (var attempt = 0; attempt < 40; attempt++)
        {
            // Same facts, same presentation order — only the surrogate keys change.
            var reIdentified = events
                .Select(e => Event(e.MatchEventTypeId, e.PlayerId, e.TeamId, e.Minute, e.MinuteExtra))
                .ToArray();

            var outcome = MatchOutcome.FromMatch(FinishedMatch(2, 1, reIdentified), SeededEventTypes());

            outcome.FirstScorerPlayerId.ShouldBe(baseline.FirstScorerPlayerId);
            outcome.FirstScorerTeamId.ShouldBe(baseline.FirstScorerTeamId);
        }
    }

    public static TheoryData<string, MatchEvent[]> IdIndependenceMatches() => new()
    {
        // Distinct scorers in the same minute: PlayerId settles the order, so an Id key inserted
        // anywhere above it would take over.
        {
            "two different scorers in the same minute",
            [
                Event(NormalGoalTypeId, PlayerY, TeamB, minute: 33),
                Event(NormalGoalTypeId, PlayerX, TeamA, minute: 33)
            ]
        },
        // Two goals in the same minute whose scorers were never recorded — both carry the default
        // Guid, so they tie on every one of the four ordering keys and only the credited team
        // tells them apart. This is the shape that an Id key appended at the very end would decide.
        {
            "two goals in the same minute with no scorer recorded",
            [
                Event(NormalGoalTypeId, UnnamedPlayer, TeamA, minute: 62),
                Event(NormalGoalTypeId, UnnamedPlayer, TeamB, minute: 62)
            ]
        }
    };

    // ---------------------------------------------------------------------------------------
    // Card counting
    // ---------------------------------------------------------------------------------------

    // Card counts are match-wide, not per-team, and the yellow/red split comes from the type Code.
    // Goals in the same match contribute to none of the three.
    [Fact]
    public void FromMatch_CardsAcrossBothTeams_CountsTheMatchTotalAndSplitsByType()
    {
        var match = FinishedMatch(2, 1,
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 12),
            Event(YellowCardTypeId, PlayerX, TeamA, minute: 25),
            Event(YellowCardTypeId, PlayerY, TeamB, minute: 40),
            Event(RedCardTypeId, PlayerZ, TeamB, minute: 58),
            Event(YellowCardTypeId, PlayerZ, TeamA, minute: 71),
            Event(PenaltyTypeId, PlayerY, TeamB, minute: 80),
            Event(RedCardTypeId, PlayerX, TeamA, minute: 88));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.TotalCards.ShouldBe(5);
        outcome.YellowCards.ShouldBe(3);
        outcome.RedCards.ShouldBe(2);
    }

    // A clean match reports zero on all three counts — an integer, never null. A member who
    // forecast 0 cards has answered correctly, and the scorer compares against these numbers.
    [Fact]
    public void FromMatch_NoCardEvents_CountsZeroOnAllThree()
    {
        var match = FinishedMatch(1, 0,
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 12));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.TotalCards.ShouldBe(0);
        outcome.YellowCards.ShouldBe(0);
        outcome.RedCards.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------------------
    // The scores themselves
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void FromMatch_CarriesTheRecordedScoresThrough()
    {
        var outcome = MatchOutcome.FromMatch(FinishedMatch(3, 2), SeededEventTypes());

        outcome.HomeScore.ShouldBe(3);
        outcome.AwayScore.ShouldBe(2);
    }

    // ---------------------------------------------------------------------------------------
    // Defensive contracts
    // ---------------------------------------------------------------------------------------

    // Unreachable from the scoring service, which gates on Status == Finished first, but
    // MatchOutcome is public Domain API. The guard exists so a score-less match can never quietly
    // become 0-0 and hand ExactScore to everyone who predicted a goalless draw.
    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, null)]
    [InlineData(null, null)]
    public void FromMatch_MatchWithoutAFinalScore_Throws(int? homeScore, int? awayScore)
        => Should.Throw<ArgumentException>(
                () => MatchOutcome.FromMatch(FinishedMatch(homeScore, awayScore), SeededEventTypes()))
            .ParamName.ShouldBe("match");

    // An event whose type is absent from the dictionary can only come from data written outside
    // the seed. It is ignored rather than guessed at: no throw, and it counts as neither goal nor
    // card — so the real goal recorded later still resolves.
    [Fact]
    public void FromMatch_EventWithAnUnmappedType_IsIgnoredEntirely()
    {
        var match = FinishedMatch(1, 0,
            Event(UnmappedTypeId, PlayerY, TeamB, minute: 5),
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 50));

        var outcome = MatchOutcome.FromMatch(match, SeededEventTypes());

        outcome.FirstScorerPlayerId.ShouldBe(PlayerX);
        outcome.FirstScorerTeamId.ShouldBe(TeamA);
        outcome.TotalCards.ShouldBe(0);
        outcome.YellowCards.ShouldBe(0);
        outcome.RedCards.ShouldBe(0);
    }

    [Fact]
    public void FromMatch_NullMatch_Throws()
        => Should.Throw<ArgumentNullException>(
                () => MatchOutcome.FromMatch(null!, SeededEventTypes()))
            .ParamName.ShouldBe("match");

    [Fact]
    public void FromMatch_NullEventTypeDictionary_Throws()
        => Should.Throw<ArgumentNullException>(
                () => MatchOutcome.FromMatch(FinishedMatch(1, 0), null!))
            .ParamName.ShouldBe("eventTypesById");
}
