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

// Player field position. Append-only: int ordinals persist — never reorder existing members.
public enum PlayerPosition
{
    Unknown = 0,
    GK = 1,
    DEF = 2,
    MID = 3,
    FWD = 4
}
