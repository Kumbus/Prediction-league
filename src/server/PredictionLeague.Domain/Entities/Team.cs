namespace PredictionLeague.Domain.Entities;

// A football team (club or national), ingested from the data API and referenced by
// Match (home/away) and Player (club/national). FR-004/FR-005.
public class Team
{
    public Guid Id { get; set; }

    // Identifier of this team in the external football data API (team.id). Null for
    // admin-created manual teams that never came through ingest.
    public int? ExternalTeamId { get; set; }

    public required string Name { get; set; }

    public string? LogoUrl { get; set; }
}
