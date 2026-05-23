namespace PredictionLeague.Models;

public enum MatchStatus
{
    Scheduled,
    Live,
    Finished
}

public enum MatchEventType
{
    Goal,
    YellowCard,
    RedCard
}

public enum MembershipRole
{
    Organizer,
    Member
}

// The match parameter a league's scoring rule awards points for.
public enum ScoringParameter
{
    ExactScore,
    CorrectOutcome,
    CorrectGoalScorer,
    CorrectCardCount
}
