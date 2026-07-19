namespace PredictionLeague.Domain.Entities;

// A player who can be credited with goals/cards (FR-005). Pre-seeded with both teams;
// referenced by MatchEvent.
public class Player
{
    public Guid Id { get; set; }

    // Identifier of this player in the external football data API (player.id).
    public int ExternalPlayerId { get; set; }

    public required string Name { get; set; }

    // Club and national teams the player belongs to; both nullable (seed may cover one).
    public Guid? ClubTeamId { get; set; }

    public Guid? NationalTeamId { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public PlayerPosition Position { get; set; } = PlayerPosition.Unknown;

    public int? HeightCm { get; set; }

    public int? NationalityId { get; set; }
}
