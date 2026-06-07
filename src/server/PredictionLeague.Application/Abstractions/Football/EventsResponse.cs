namespace PredictionLeague.Application.Abstractions.Football;

// Provider-neutral result of an events fetch. NoContent is true for a 204 (events before
// kickoff) — a valid empty result, not an error.
public sealed record EventsResponse(IReadOnlyList<IngestEvent> Events, bool NoContent, RateLimitSnapshot RateLimit);

// One event, flattened to what ingest needs. Trailing items can be partial (type/player
// null) — guarded by the ingest service.
public sealed record IngestEvent(
    int? Minute,
    int? MinuteExtra,
    IngestTeamRef? Team,
    int? PlayerId,
    string? PlayerName,
    string? Type,
    string? Detail);
