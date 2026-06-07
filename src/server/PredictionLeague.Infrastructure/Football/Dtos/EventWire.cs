using System.Text.Json.Serialization;

namespace PredictionLeague.Infrastructure.Football.Dtos;

// API-Football GET /fixtures/events wire shape. Internal to the client — mapped to the
// provider-neutral IngestEvent before crossing the Application boundary.
internal sealed record EventItem(
    [property: JsonPropertyName("time")] EventTime? Time,
    [property: JsonPropertyName("team")] TeamRefWire? Team,
    [property: JsonPropertyName("player")] PlayerRefWire? Player,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("detail")] string? Detail);

internal sealed record EventTime(
    [property: JsonPropertyName("elapsed")] int? Elapsed,
    [property: JsonPropertyName("extra")] int? Extra);

internal sealed record PlayerRefWire(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("name")] string? Name);
