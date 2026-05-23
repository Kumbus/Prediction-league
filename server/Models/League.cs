namespace PredictionLeague.Models;

// A private pool tied to one tournament (FR-006) with organizer-defined scoring (FR-008).
public class League
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public Guid TournamentId { get; set; }

    public Guid OrganizerUserId { get; set; }

    // Shared with friends to join (FR-007).
    public required string InviteCode { get; set; }

    public ICollection<ScoringRule> ScoringRules { get; set; } = [];

    public ICollection<LeagueMembership> Memberships { get; set; } = [];
}

public class LeagueMembership
{
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public Guid UserId { get; set; }

    public MembershipRole Role { get; set; }
}
