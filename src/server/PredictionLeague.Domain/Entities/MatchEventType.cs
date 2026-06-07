namespace PredictionLeague.Domain.Entities;

// Dictionary of match-event sub-types (Normal Goal / Own Goal / Penalty / Missed
// Penalty / Yellow Card / Red Card) carrying a category usable by scoring. Replaces the
// coarse MatchEventType enum. FR-005.
public class MatchEventType
{
    // Stable, seeded primary key (referenced by MatchEvent.MatchEventTypeId).
    public int Id { get; set; }

    // Space-free code (e.g. "NormalGoal"); mapped from the API event detail.
    public required string Code { get; set; }

    public required string DisplayName { get; set; }

    public MatchEventCategory Category { get; set; }
}
