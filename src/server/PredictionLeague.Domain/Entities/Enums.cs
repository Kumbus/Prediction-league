namespace PredictionLeague.Domain.Entities;

public enum MatchStatus
{
    Scheduled,
    Live,
    Finished
}

// Category a MatchEventType dictionary row belongs to; usable by scoring.
public enum MatchEventCategory
{
    Goal,
    Card,
    Other
}

public enum MembershipRole
{
    Organizer,
    Member
}

// The match parameter a league's scoring rule awards points for.
// Append-only: int ordinals persist — never reorder existing members.
public enum ScoringParameter
{
    ExactScore,
    CorrectOutcome,
    CorrectGoalScorer,
    CorrectCardCount,
    CorrectYellowCards,
    CorrectRedCards
}
