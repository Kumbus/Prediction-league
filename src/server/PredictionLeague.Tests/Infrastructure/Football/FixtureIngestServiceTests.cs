using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Scoring;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Football;
using Shouldly;
using static PredictionLeague.Tests.Domain.Scoring.ScoringFixtures;

namespace PredictionLeague.Tests.Infrastructure.Football;

// The ingest run's partial-success contract.
//
// Scoring runs per fixture, after that fixture's save commits, and a throw there must not abort the
// rest of the run — a partial ingest already leaves each processed match consistent. That decision
// is sound; what it must not do is leave the failure in a log line and nowhere else. An ingest that
// answers "N fixtures, M events" while some of those matches carry stale points is the swallowed
// error every caller then reports as a success: the endpoint returns 200, the admin page renders
// the counts, and the timer logs "Ingested tournament ...".
//
// So the run reports which matches did not score, and these tests assert on that list — never on
// "the call did not throw", which passes against exactly the behaviour they exist to catch.
public class FixtureIngestServiceTests
{
    private static readonly Guid TournamentId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    // Two finished fixtures, scoring throws for the second. The run finishes both — and says so —
    // but the result names the one whose points are stale, because the remedy is per match
    // (POST /api/matches/{id}/rescore) and a bare count could not address it.
    [Fact]
    public async Task IngestTournamentAsync_ScoringThrowsForOneFixture_NamesThatMatchInTheResult()
    {
        var harness = new IngestHarness().WithFixtures(Fixture(1001), Fixture(1002));
        var unscored = harness.FailScoringForFixture(1002);

        var result = await harness.Build().IngestTournamentAsync(TournamentId, season: 2026, date: null);

        // The run completed: a scoring failure aborts neither the fixture's own save nor the loop.
        result.FixturesUpserted.ShouldBe(2);

        result.UnscoredMatchIds.ShouldBe(new[] { await unscored });
    }

    // The other side of the same contract: a clean run reports an empty list, so a caller can read
    // "no ids" as "every match scored" rather than as "nobody looked".
    [Fact]
    public async Task IngestTournamentAsync_ScoringSucceeds_ReportsNoUnscoredMatches()
    {
        var harness = new IngestHarness().WithFixtures(Fixture(1001), Fixture(1002));

        var result = await harness.Build().IngestTournamentAsync(TournamentId, season: 2026, date: null);

        result.FixturesUpserted.ShouldBe(2);
        result.UnscoredMatchIds.ShouldBeEmpty();
    }

    // Cancellation is not a scoring failure and must not be reported as one — the run is being torn
    // down, so it propagates rather than logging one match as unscored and pressing on.
    [Fact]
    public async Task IngestTournamentAsync_ScoringCancelled_PropagatesRatherThanReportingUnscored()
    {
        var harness = new IngestHarness().WithFixtures(Fixture(1001));
        harness.Scoring
            .ScoreMatchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<MatchScoringResult>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Build().IngestTournamentAsync(TournamentId, season: 2026, date: null));
    }

    // ---------------------------------------------------------------------------------------
    // Dropped events: the same verdict one layer down
    // ---------------------------------------------------------------------------------------

    // Four goal/card events reach the mapper and one survives. The other three are each a scoring
    // input that went missing: an API detail with no dictionary row ("Second Yellow card" — a red
    // card by any league's rules), a card with no player, a goal with no team. The match still
    // scores, and CorrectGoalScorer / the card rules read only what survived — so a run reporting
    // EventsUpserted alone cannot be told apart from one that dropped nothing.
    [Fact]
    public async Task IngestTournamentAsync_EventsTheMapperCannotPersist_ReportsThemAsDropped()
    {
        var harness = new IngestHarness()
            .WithFixtures(Fixture(1001))
            .WithEvents(1001,
                Goal(minute: 12, detail: "Normal Goal"),
                Card(minute: 55, detail: "Second Yellow card"),           // no dictionary row
                Card(minute: 61, detail: "Yellow Card") with { PlayerId = null },
                Goal(minute: 78, detail: "Penalty") with { Team = null });

        var matchId = harness.MatchIdForFixture(1001);

        var result = await harness.Build().IngestTournamentAsync(TournamentId, season: 2026, date: null);

        result.EventsUpserted.ShouldBe(1);
        result.DroppedEvents.ShouldBe(3);
        result.MatchesWithDroppedEvents.ShouldBe(new[] { await matchId });
    }

    // Substitutions and VAR checks are not modelled at all — no MatchEventTypeId exists for them,
    // and no scoring rule reads them. Filtering those is not a loss, and counting them as dropped
    // would put a permanent false alarm on every ingest of a real fixture.
    [Fact]
    public async Task IngestTournamentAsync_SubstitutionsAndVar_AreFilteredRatherThanReportedAsDropped()
    {
        var harness = new IngestHarness()
            .WithFixtures(Fixture(1001))
            .WithEvents(1001,
                Goal(minute: 12, detail: "Normal Goal"),
                Goal(minute: 46, detail: "Substitution 1") with { Type = "subst" },
                Goal(minute: 70, detail: "Goal cancelled") with { Type = "Var" });

        var result = await harness.Build().IngestTournamentAsync(TournamentId, season: 2026, date: null);

        result.EventsUpserted.ShouldBe(1);
        result.DroppedEvents.ShouldBe(0);
        result.MatchesWithDroppedEvents.ShouldBeEmpty();
    }

    private static IngestEvent Goal(int minute, string detail) => new(
        Minute: minute,
        MinuteExtra: null,
        Team: new IngestTeamRef(10010, "Home 1001", null),
        PlayerId: 5001,
        PlayerName: "A Player",
        Type: "Goal",
        Detail: detail);

    private static IngestEvent Card(int minute, string detail) =>
        Goal(minute, detail) with { Type = "Card" };

    // A finished fixture with a recorded score — the shape that actually reaches scoring. Its
    // events call is stubbed empty on the harness, so nothing here depends on the events path.
    private static IngestFixture Fixture(int fixtureId) => new(
        FixtureId: fixtureId,
        KickoffUtc: new DateTimeOffset(2026, 6, 14, 18, 0, 0, TimeSpan.Zero),
        StatusShort: "FT",
        Season: 2026,
        Round: "Group Stage - 1",
        Home: new IngestTeamRef(fixtureId * 10, "Home " + fixtureId, null),
        Away: new IngestTeamRef(fixtureId * 10 + 1, "Away " + fixtureId, null),
        GoalsHome: 2,
        GoalsAway: 1,
        FulltimeHome: 2,
        FulltimeAway: 1);

    private sealed class IngestHarness
    {
        public IFootballApiClient Api { get; } = Substitute.For<IFootballApiClient>();
        public ITournamentRepository Tournaments { get; } = Substitute.For<ITournamentRepository>();
        public IMatchRepository Matches { get; } = Substitute.For<IMatchRepository>();
        public ITeamRepository Teams { get; } = Substitute.For<ITeamRepository>();
        public IPlayerRepository Players { get; } = Substitute.For<IPlayerRepository>();
        public IMatchEventTypeRepository EventTypes { get; } = Substitute.For<IMatchEventTypeRepository>();
        public IMatchScoringService Scoring { get; } = Substitute.For<IMatchScoringService>();

        // The Match id the service minted for each external fixture id, captured off AddAsync: the
        // ids are created inside the service, so this is the only way a test can name one.
        private readonly Dictionary<int, TaskCompletionSource<Guid>> _matchIdByFixture = [];

        public IngestHarness()
        {
            Tournaments.GetByIdAsync(TournamentId, Arg.Any<CancellationToken>())
                .Returns(new Tournament { Id = TournamentId, Name = "Euro 2026", ExternalApiId = "4", Season = 2026 });

            // The real dictionary, verbatim from the seed — the mapper looks rows up by Code, so
            // an invented set would make "unmapped detail" mean nothing.
            EventTypes.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns<IReadOnlyList<MatchEventType>>(SeededEventTypes().Values.ToList());

            // No events unless a test asks for them.
            Api.GetFixtureEventsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new EventsResponse([], NoContent: true, RateLimitSnapshot.Unknown));

            Matches.AddAsync(Arg.Do<Match>(m => Pending(m.ExternalFixtureId!.Value).TrySetResult(m.Id)),
                Arg.Any<CancellationToken>());
        }

        public IngestHarness WithFixtures(params IngestFixture[] fixtures)
        {
            Api.GetFixturesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new FixturesResponse(fixtures, new RateLimitSnapshot(DailyRemaining: 90, MinuteRemaining: 9)));
            return this;
        }

        public IngestHarness WithEvents(int fixtureId, params IngestEvent[] events)
        {
            Api.GetFixtureEventsAsync(fixtureId, Arg.Any<CancellationToken>())
                .Returns(new EventsResponse(events, NoContent: false, RateLimitSnapshot.Unknown));
            return this;
        }

        // The id the service minted for a fixture's match. Awaited after the run, once AddAsync
        // has supplied it.
        public Task<Guid> MatchIdForFixture(int fixtureId) => Pending(fixtureId).Task;

        // Makes ScoreMatchAsync throw for one fixture's match, and hands back that match's id for
        // the assertion. Awaited after the run, once AddAsync has minted it.
        public Task<Guid> FailScoringForFixture(int fixtureId)
        {
            var wanted = Pending(fixtureId);

            Scoring.ScoreMatchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => wanted.Task.IsCompleted && call.Arg<Guid>() == wanted.Task.Result
                    ? throw new InvalidOperationException("Scoring blew up.")
                    : MatchScoringResult.None);

            return wanted.Task;
        }

        private TaskCompletionSource<Guid> Pending(int fixtureId)
        {
            if (!_matchIdByFixture.TryGetValue(fixtureId, out var tcs))
                _matchIdByFixture[fixtureId] = tcs = new TaskCompletionSource<Guid>();
            return tcs;
        }

        public FixtureIngestService Build() => new(
            Api, Tournaments, Matches, Teams, Players, EventTypes, Scoring,
            NullLogger<FixtureIngestService>.Instance);
    }
}
