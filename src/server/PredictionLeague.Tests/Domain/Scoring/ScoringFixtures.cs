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
    public static readonly Guid TeamA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TeamB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

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
