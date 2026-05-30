namespace PredictionLeague.Domain.Entities;

// A fixture and its result. Granular events drive scoring (FR-005).
public class Match
{
    public Guid Id { get; set; }

    public Guid TournamentId { get; set; }

    public required string HomeTeam { get; set; }

    public required string AwayTeam { get; set; }

    // Stored UTC; predictions lock at this instant (FR-010).
    public DateTimeOffset KickoffUtc { get; set; }

    public MatchStatus Status { get; set; }

    public int? HomeScore { get; set; }

    public int? AwayScore { get; set; }

    public ICollection<MatchEvent> Events { get; set; } = [];
}

public class MatchEvent
{
    public Guid Id { get; set; }

    public Guid MatchId { get; set; }

    public MatchEventType Type { get; set; }

    // Player credited with the goal or carded; from the data API.
    public required string Player { get; set; }

    public int Minute { get; set; }
}
