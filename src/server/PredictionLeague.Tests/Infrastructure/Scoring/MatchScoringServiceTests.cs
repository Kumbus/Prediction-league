using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Scoring;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Scoring;
using Shouldly;
using static PredictionLeague.Tests.Domain.Scoring.ScoringFixtures;

namespace PredictionLeague.Tests.Infrastructure.Scoring;

// Risk #2: two leagues on one tournament, scoring one identical forecast, must produce two
// different totals — each correct for its own configuration. Everything else in the scoring path
// is shared (one MatchOutcome, one pure PredictionScorer); the divergence lives entirely in the
// per-league rule lookup inside ScoreMatchAsync.
//
// The service computes and never mutates a tracked entity, so the dictionary handed to
// SetAwardedPointsAsync is the whole observable output. Every assertion below reads it.
public class MatchScoringServiceTests
{
    // ---------------------------------------------------------------------------------------
    // The wedge: per-league scoring
    // ---------------------------------------------------------------------------------------

    // Two leagues on the same tournament, two members who submitted the *same* forecast, one match.
    //
    // The match finished 2-1 with PlayerX (team A) scoring first. Both forecasts say 2-1, PlayerX
    // for team A — so both are right about the scoreline, the result and the first scorer.
    //
    //   "Scoreline Cup" configures ExactScore = 5 and nothing else       -> 5
    //   "Pundits League" configures CorrectOutcome = 2, CorrectGoalScorer = 4, no ExactScore
    //                                                                    -> 2 + 4 = 6
    //
    // Both totals are asserted specifically. Asserting only that they differ would be satisfied by
    // any two wrong numbers that happen not to be equal.
    [Fact]
    public async Task ScoreMatchAsync_TwoLeaguesOnOneTournament_ScoreOneForecastByEachLeaguesOwnRules()
    {
        var match = FinishedMatch(2, 1, Event(NormalGoalTypeId, PlayerX, TeamA, minute: 10));

        var scorelineCup = LeagueWith(match.TournamentId, "Scoreline Cup",
            (ScoringParameter.ExactScore, 5));

        var punditsLeague = LeagueWith(match.TournamentId, "Pundits League",
            (ScoringParameter.CorrectOutcome, 2),
            (ScoringParameter.CorrectGoalScorer, 4));

        var inScorelineCup = Forecast(2, 1, PlayerX, TeamA, leagueId: scorelineCup.Id, matchId: match.Id);
        var inPunditsLeague = Forecast(2, 1, PlayerX, TeamA, leagueId: punditsLeague.Id, matchId: match.Id);

        var harness = new ScoringHarness()
            .WithMatch(match)
            .WithSeededEventTypes()
            .WithPredictions(inScorelineCup, inPunditsLeague)
            .WithLeagues(scorelineCup, punditsLeague);

        var result = await harness.Build().ScoreMatchAsync(match.Id);

        harness.AwardedPoints.ShouldNotBeNull();
        harness.AwardedPoints[inScorelineCup.Id].ShouldBe(5);
        harness.AwardedPoints[inPunditsLeague.Id].ShouldBe(6);

        result.PredictionsScored.ShouldBe(2);
        result.LeaguesTouched.ShouldBe(2);
    }

    // A league whose organizer configured no rules is a legitimate state, not an error and not a
    // skip. Its members are scored and earn nothing — an integer 0, never null, because null means
    // "not scored" and standings depend on the distinction.
    //
    // Two ways a league ends up with no rules, both covered here: it is on the tournament with an
    // empty rule collection, and it is not among the tournament's leagues at all.
    [Fact]
    public async Task ScoreMatchAsync_LeagueWithNoRules_ScoresZeroRatherThanLeavingUnscored()
    {
        var match = FinishedMatch(2, 1, Event(NormalGoalTypeId, PlayerX, TeamA, minute: 10));

        var scorelineCup = LeagueWith(match.TournamentId, "Scoreline Cup",
            (ScoringParameter.ExactScore, 5));

        var newcomers = LeagueWith(match.TournamentId, "Newcomers"); // no rules configured

        var inScorelineCup = Forecast(2, 1, PlayerX, TeamA, leagueId: scorelineCup.Id, matchId: match.Id);
        var inNewcomers = Forecast(2, 1, PlayerX, TeamA, leagueId: newcomers.Id, matchId: match.Id);
        var inAnUnlistedLeague = Forecast(2, 1, PlayerX, TeamA, leagueId: Guid.NewGuid(), matchId: match.Id);

        var harness = new ScoringHarness()
            .WithMatch(match)
            .WithSeededEventTypes()
            .WithPredictions(inScorelineCup, inNewcomers, inAnUnlistedLeague)
            .WithLeagues(scorelineCup, newcomers);

        await harness.Build().ScoreMatchAsync(match.Id);

        harness.AwardedPoints.ShouldNotBeNull();

        harness.AwardedPoints[inNewcomers.Id].HasValue.ShouldBeTrue();
        harness.AwardedPoints[inNewcomers.Id].ShouldBe(0);

        harness.AwardedPoints[inAnUnlistedLeague.Id].HasValue.ShouldBeTrue();
        harness.AwardedPoints[inAnUnlistedLeague.Id].ShouldBe(0);

        // The configured league is unaffected by its rule-less neighbours.
        harness.AwardedPoints[inScorelineCup.Id].ShouldBe(5);
    }

    // Every prediction on the match is scored in the one write, not just the first league's.
    [Fact]
    public async Task ScoreMatchAsync_WritesOneEntryPerPrediction()
    {
        var match = FinishedMatch(2, 1, Event(NormalGoalTypeId, PlayerX, TeamA, minute: 10));
        var league = LeagueWith(match.TournamentId, "Scoreline Cup", (ScoringParameter.ExactScore, 5));

        var right = Forecast(2, 1, leagueId: league.Id, matchId: match.Id);
        var wrong = Forecast(0, 0, leagueId: league.Id, matchId: match.Id);

        var harness = new ScoringHarness()
            .WithMatch(match)
            .WithSeededEventTypes()
            .WithPredictions(right, wrong)
            .WithLeagues(league);

        var result = await harness.Build().ScoreMatchAsync(match.Id);

        harness.AwardedPoints.ShouldNotBeNull();
        harness.AwardedPoints.Count.ShouldBe(2);
        harness.AwardedPoints[right.Id].ShouldBe(5);
        harness.AwardedPoints[wrong.Id].ShouldBe(0);

        // Both predictions belong to the same league.
        result.LeaguesTouched.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------------------
    // Un-scoring
    // ---------------------------------------------------------------------------------------

    // Reverting a result has to take its points with it. A match that is no longer a finished
    // result un-scores every prediction to null — not 0, which would read as "played and earned
    // nothing", and not stale points, which would leave standings asserting something the recorded
    // result no longer says.
    [Theory]
    [InlineData(MatchStatus.Scheduled, null, null)] // never played
    [InlineData(MatchStatus.Live, 1, 0)]            // in progress, scores provisional
    [InlineData(MatchStatus.Finished, null, 1)]     // finished but the home score was cleared
    [InlineData(MatchStatus.Finished, 2, null)]     // finished but the away score was cleared
    public async Task ScoreMatchAsync_MatchIsNotAFinishedResult_UnScoresEveryPrediction(
        MatchStatus status, int? homeScore, int? awayScore)
    {
        var match = MatchInStatus(status, homeScore, awayScore,
            Event(NormalGoalTypeId, PlayerX, TeamA, minute: 10));

        var league = LeagueWith(match.TournamentId, "Scoreline Cup", (ScoringParameter.ExactScore, 5));

        var perfect = Forecast(1, 0, PlayerX, TeamA, leagueId: league.Id, matchId: match.Id);
        var hopeless = Forecast(4, 4, leagueId: league.Id, matchId: match.Id);

        var harness = new ScoringHarness()
            .WithMatch(match)
            .WithPredictions(perfect, hopeless);

        var result = await harness.Build().ScoreMatchAsync(match.Id);

        harness.AwardedPoints.ShouldNotBeNull();
        harness.AwardedPoints[perfect.Id].ShouldBeNull();
        harness.AwardedPoints[hopeless.Id].ShouldBeNull();

        result.PredictionsScored.ShouldBe(2);
    }

    // ---------------------------------------------------------------------------------------
    // Early exits
    // ---------------------------------------------------------------------------------------

    // A match id that resolves to nothing is a no-op result, not an exception — and above all, not
    // a write. Nothing downstream should see a scoring pass that happened against no match.
    [Fact]
    public async Task ScoreMatchAsync_MatchNotFound_ReturnsNoneAndWritesNothing()
    {
        var harness = new ScoringHarness().WithMatch(null);

        var result = await harness.Build().ScoreMatchAsync(Guid.NewGuid());

        result.ShouldBe(MatchScoringResult.None);
        await harness.Predictions.DidNotReceiveWithAnyArgs()
            .SetAwardedPointsAsync(default, default!, default);
    }

    // Nobody forecast this match, so there is nothing to write. The guard matters because the
    // service is called from every result-changing path, including ingest looping a whole
    // tournament — most of those matches have no predictions in most leagues.
    [Fact]
    public async Task ScoreMatchAsync_NoPredictions_ReturnsNoneAndWritesNothing()
    {
        var match = FinishedMatch(2, 1, Event(NormalGoalTypeId, PlayerX, TeamA, minute: 10));

        var harness = new ScoringHarness()
            .WithMatch(match)
            .WithPredictions();

        var result = await harness.Build().ScoreMatchAsync(match.Id);

        result.ShouldBe(MatchScoringResult.None);
        await harness.Predictions.DidNotReceiveWithAnyArgs()
            .SetAwardedPointsAsync(default, default!, default);
    }

    // ---------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------

    // The four repository interfaces carry far more members than the service touches — hand-rolled
    // fakes would mean dozens of dead stubs that break the build every time a slice adds a method.
    // Each With* call configures exactly one of the five methods the service can call, so a test
    // that stubs a method proves that method actually runs on its path.
    private sealed class ScoringHarness
    {
        public IMatchRepository Matches { get; } = Substitute.For<IMatchRepository>();
        public ILeagueRepository Leagues { get; } = Substitute.For<ILeagueRepository>();
        public IPredictionRepository Predictions { get; } = Substitute.For<IPredictionRepository>();
        public IMatchEventTypeRepository EventTypes { get; } = Substitute.For<IMatchEventTypeRepository>();

        // The dictionary the service handed to SetAwardedPointsAsync — its entire observable output.
        public IReadOnlyDictionary<Guid, int?>? AwardedPoints { get; private set; }

        public ScoringHarness WithMatch(Match? match)
        {
            Matches.GetWithEventsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(match);
            return this;
        }

        public ScoringHarness WithPredictions(params Prediction[] predictions)
        {
            Predictions.ListForMatchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns<IReadOnlyList<Prediction>>(predictions);

            if (predictions.Length > 0) CaptureAwardedPoints();

            return this;
        }

        public ScoringHarness WithLeagues(params League[] leagues)
        {
            Leagues.ListByTournamentWithRulesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns<IReadOnlyList<League>>(leagues);
            return this;
        }

        public ScoringHarness WithSeededEventTypes()
        {
            EventTypes.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns<IReadOnlyList<MatchEventType>>(SeededEventTypes().Values.ToList());
            return this;
        }

        public MatchScoringService Build() => new(
            Matches,
            Leagues,
            Predictions,
            EventTypes,
            NullLogger<MatchScoringService>.Instance);

        private void CaptureAwardedPoints()
            => Predictions
                .SetAwardedPointsAsync(
                    Arg.Any<Guid>(),
                    Arg.Do<IReadOnlyDictionary<Guid, int?>>(points => AwardedPoints = points),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
    }
}
