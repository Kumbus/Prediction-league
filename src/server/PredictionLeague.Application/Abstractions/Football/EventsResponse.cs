using System.Text.Json.Serialization;

namespace PredictionLeague.Application.Abstractions.Football;

// Typed result of GET /fixtures/events. NoContent is true for a 204 (events before
// kickoff) — a valid empty result, not an error.
public sealed record EventsResponse(IReadOnlyList<EventDto> Events, bool NoContent, RateLimitSnapshot RateLimit);

// One /fixtures/events response item. Trailing items can be partial (type/player null) —
// guarded by the ingest service.
public sealed record EventDto(
    [property: JsonPropertyName("time")] EventTimeDto? Time,
    [property: JsonPropertyName("team")] TeamRefDto? Team,
    [property: JsonPropertyName("player")] PlayerRefDto? Player,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("detail")] string? Detail);

public sealed record EventTimeDto(
    [property: JsonPropertyName("elapsed")] int? Elapsed,
    [property: JsonPropertyName("extra")] int? Extra);

public sealed record PlayerRefDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("name")] string? Name);
