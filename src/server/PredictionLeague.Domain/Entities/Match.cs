namespace PredictionLeague.Domain.Entities;

// A fixture and its result. Granular events drive scoring (FR-005).
public class Match
{
    public Guid Id { get; set; }

    public Guid TournamentId { get; set; }

    // API fixture id — idempotent upsert key (FR-004). Null for admin-entered manual matches.
    public int? ExternalFixtureId { get; set; }

    // API league.season — needed to re-query the fixture.
    public int Season { get; set; }

    // API league.round (e.g. "Regular Season - 14").
    public required string Round { get; set; }

    public Guid HomeTeamId { get; set; }

    public Guid AwayTeamId { get; set; }

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

    // Dictionary sub-type (Normal Goal / Yellow Card / ...). FR-005.
    public int MatchEventTypeId { get; set; }

    // Player credited with the goal or carded.
    public Guid PlayerId { get; set; }

    // Team the event is attributed to.
    public Guid TeamId { get; set; }

    public int Minute { get; set; }

    // API time.extra — added/stoppage-time minutes, when present.
    public int? MinuteExtra { get; set; }
}
