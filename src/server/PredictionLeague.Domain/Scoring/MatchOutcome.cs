using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Domain.Scoring;

// What actually happened in a match, in the shape scoring needs (FR-011): the final scores plus
// the goal and card facts derived from the event list. Deliberately separate from Match/MatchEvent
// so the engine stays free of persistence concerns and the whole scoring input fits on one screen.
public sealed record MatchOutcome(
    int HomeScore,
    int AwayScore,
    // The pair a CorrectGoalScorer forecast is compared against. Both halves are null together:
    // either a qualifying goal was recorded or none was.
    Guid? FirstScorerPlayerId,
    Guid? FirstScorerTeamId,
    int TotalCards,
    int YellowCards,
    int RedCards)
{
    // A shot, not a goal — but seeded with Category = Goal (MatchEventTypeConfiguration.cs:24), so
    // first-scorer resolution has to exclude it by Code and cannot trust the category alone.
    public const string MissedPenaltyCode = "MissedPenalty";

    public const string YellowCardCode = "YellowCard";

    public const string RedCardCode = "RedCard";

    // Builds the outcome from a finished match and its events. Scores must be present: the caller
    // decides what an unfinished or score-less match means (the scoring service un-scores it), and
    // silently substituting 0-0 here would award ExactScore to everyone who predicted a goalless draw.
    public static MatchOutcome FromMatch(
        Match match,
        IReadOnlyDictionary<int, MatchEventType> eventTypesById)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(eventTypesById);

        if (match.HomeScore is not { } homeScore || match.AwayScore is not { } awayScore)
            throw new ArgumentException(
                $"Match '{match.Id}' has no final score; it cannot be turned into a MatchOutcome.",
                nameof(match));

        // Events whose type is not in the dictionary are ignored rather than guessed at — an
        // unmapped id can only come from data written outside the seeded dictionary.
        var typed = match.Events
            .Select(e => (Event: e, Type: eventTypesById.GetValueOrDefault(e.MatchEventTypeId)))
            .Where(x => x.Type is not null)
            .Select(x => (x.Event, Type: x.Type!))
            .ToList();

        // Ordering key — load-bearing. Every component is admin-entered data, so the same recorded
        // facts always name the same first scorer. MatchEvent.Id must never appear here: it is a
        // fresh Guid on every replace-all save, so an Id tie-break would move CorrectGoalScorer
        // points between members on a no-op re-save. Sorted in memory rather than in SQL, which
        // also keeps the comparison off SQL Server's uniqueidentifier collation (it does not match
        // Guid.CompareTo). A null MinuteExtra means "no stoppage time" and sorts as 0, ahead of 90+1.
        var firstGoal = typed
            .Where(x => x.Type.Category == MatchEventCategory.Goal
                        && !string.Equals(x.Type.Code, MissedPenaltyCode, StringComparison.Ordinal))
            .OrderBy(x => x.Event.Minute)
            .ThenBy(x => x.Event.MinuteExtra ?? 0)
            .ThenBy(x => x.Event.MatchEventTypeId)
            .ThenBy(x => x.Event.PlayerId)
            .Select(x => x.Event)
            .FirstOrDefault();

        var cards = typed.Where(x => x.Type.Category == MatchEventCategory.Card).ToList();

        return new MatchOutcome(
            homeScore,
            awayScore,
            firstGoal?.PlayerId,
            firstGoal?.TeamId,
            cards.Count,
            cards.Count(x => string.Equals(x.Type.Code, YellowCardCode, StringComparison.Ordinal)),
            cards.Count(x => string.Equals(x.Type.Code, RedCardCode, StringComparison.Ordinal)));
    }
}
