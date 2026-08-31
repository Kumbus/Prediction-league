using PredictionLeague.Domain.Entities;
using PredictionLeague.Domain.Scoring;

namespace PredictionLeague.Tests.Domain.Scoring;

// Construction helpers for the scoring theories. Deliberately free of default *point* values:
// every rule's Points comes from the caller, so a test can never pass while quietly reading a
// number the league under test never configured.
internal static class ScoringFixtures
{
    // Fixed ids so a failure message names the same player and team every run.
    public static readonly Guid PlayerX = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid PlayerY = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid PlayerZ = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid TeamA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TeamB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // MatchEvent.PlayerId is non-nullable, so an event whose scorer the admin did not record
    // carries the default Guid. Two such goals in the same minute tie on every ordering key.
    public static readonly Guid UnnamedPlayer = Guid.Empty;

    // The seeded MatchEventType ids, verbatim from MatchEventTypeConfiguration.HasData. Const so
    // they can be used as [InlineData] arguments. A fixture that invented its own ids would pass
    // while testing nothing real — MatchOutcome.FromMatch looks types up by exactly these numbers.
    public const int NormalGoalTypeId = 1;
    public const int OwnGoalTypeId = 2;
    public const int PenaltyTypeId = 3;
    public const int MissedPenaltyTypeId = 4;
    public const int YellowCardTypeId = 5;
    public const int RedCardTypeId = 6;

    // An id no seeded row uses, for the "event type absent from the dictionary" case.
    public const int UnmappedTypeId = 99;

    // The six seeded dictionary rows, ids, codes and categories exactly as
    // MatchEventTypeConfiguration.cs seeds them. Note MissedPenalty sits under Category = Goal:
    // that is the wart first-scorer resolution has to exclude by Code.
    public static IReadOnlyDictionary<int, MatchEventType> SeededEventTypes()
        => new Dictionary<int, MatchEventType>
        {
            [NormalGoalTypeId] = new() { Id = NormalGoalTypeId, Code = "NormalGoal", DisplayName = "Normal Goal", Category = MatchEventCategory.Goal },
            [OwnGoalTypeId] = new() { Id = OwnGoalTypeId, Code = "OwnGoal", DisplayName = "Own Goal", Category = MatchEventCategory.Goal },
            [PenaltyTypeId] = new() { Id = PenaltyTypeId, Code = "Penalty", DisplayName = "Penalty", Category = MatchEventCategory.Goal },
            [MissedPenaltyTypeId] = new() { Id = MissedPenaltyTypeId, Code = "MissedPenalty", DisplayName = "Missed Penalty", Category = MatchEventCategory.Goal },
            [YellowCardTypeId] = new() { Id = YellowCardTypeId, Code = "YellowCard", DisplayName = "Yellow Card", Category = MatchEventCategory.Card },
            [RedCardTypeId] = new() { Id = RedCardTypeId, Code = "RedCard", DisplayName = "Red Card", Category = MatchEventCategory.Card }
        };

    // One recorded event. The Id is a fresh Guid on every call, exactly as a replace-all save
    // mints one — nothing downstream is allowed to depend on its value.
    public static MatchEvent Event(
        int matchEventTypeId,
        Guid playerId,
        Guid teamId,
        int minute,
        int? minuteExtra = null)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            MatchEventTypeId = matchEventTypeId,
            PlayerId = playerId,
            TeamId = teamId,
            Minute = minute,
            MinuteExtra = minuteExtra
        };

    // A played-out match. Scores are nullable so the "unfinished match" guard can be exercised.
    public static Match FinishedMatch(int? homeScore, int? awayScore, params MatchEvent[] events)
        => new()
        {
            Id = Guid.NewGuid(),
            TournamentId = Guid.NewGuid(),
            Season = 2026,
            Round = "Regular Season - 1",
            HomeTeamId = TeamA,
            AwayTeamId = TeamB,
            KickoffUtc = new DateTimeOffset(2026, 8, 15, 18, 0, 0, TimeSpan.Zero),
            Status = MatchStatus.Finished,
            HomeScore = homeScore,
            AwayScore = awayScore,
            Events = events.ToList()
        };

    // One league's scoring configuration. Parameters absent from the list are unconfigured, which
    // is the only way a league says "this does not count".
    public static IReadOnlyList<ScoringRule> Rules(params (ScoringParameter Parameter, int Points)[] configured)
    {
        var leagueId = Guid.NewGuid();

        return configured
            .Select(c => new ScoringRule
            {
                Id = Guid.NewGuid(),
                LeagueId = leagueId,
                Parameter = c.Parameter,
                Points = c.Points
            })
            .ToList();
    }

    // A member's forecast. Every optional half defaults to null — a blank forecast — so each test
    // states only the fields it is about.
    public static Prediction Forecast(
        int homeScore,
        int awayScore,
        Guid? firstScorerPlayerId = null,
        Guid? firstScorerTeamId = null,
        int? totalCards = null,
        int? yellowCards = null,
        int? redCards = null)
        => new()
        {
            Id = Guid.NewGuid(),
            LeagueId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PredictedHomeScore = homeScore,
            PredictedAwayScore = awayScore,
            PredictedFirstScorerPlayerId = firstScorerPlayerId,
            PredictedFirstScorerTeamId = firstScorerTeamId,
            PredictedTotalCards = totalCards,
            PredictedYellowCards = yellowCards,
            PredictedRedCards = redCards
        };

    // What actually happened. Card counts default to a clean match; the goal-scorer pair defaults
    // to "no qualifying goal was recorded", which is how MatchOutcome represents a goalless match.
    public static MatchOutcome Result(
        int homeScore,
        int awayScore,
        Guid? firstScorerPlayerId = null,
        Guid? firstScorerTeamId = null,
        int totalCards = 0,
        int yellowCards = 0,
        int redCards = 0)
        => new(
            homeScore,
            awayScore,
            firstScorerPlayerId,
            firstScorerTeamId,
            totalCards,
            yellowCards,
            redCards);
}
