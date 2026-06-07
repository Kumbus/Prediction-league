using System.Text.Json.Serialization;

namespace PredictionLeague.Infrastructure.Football.Dtos;

// API-Football GET /fixtures wire shape. Internal to the client — mapped to the
// provider-neutral IngestFixture before crossing the Application boundary.
internal sealed record FixtureItem(
    [property: JsonPropertyName("fixture")] FixtureInfo? Fixture,
    [property: JsonPropertyName("league")] FixtureLeague? League,
    [property: JsonPropertyName("teams")] FixtureTeams? Teams,
    [property: JsonPropertyName("goals")] Goals? Goals,
    [property: JsonPropertyName("score")] Score? Score);

internal sealed record FixtureInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("status")] FixtureStatus? Status);

internal sealed record FixtureStatus(
    [property: JsonPropertyName("short")] string? Short,
    [property: JsonPropertyName("elapsed")] int? Elapsed,
    [property: JsonPropertyName("extra")] int? Extra);

internal sealed record FixtureLeague(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("season")] int Season,
    [property: JsonPropertyName("round")] string? Round);

internal sealed record FixtureTeams(
    [property: JsonPropertyName("home")] TeamRefWire? Home,
    [property: JsonPropertyName("away")] TeamRefWire? Away);

internal sealed record TeamRefWire(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("logo")] string? Logo);

internal sealed record Goals(
    [property: JsonPropertyName("home")] int? Home,
    [property: JsonPropertyName("away")] int? Away);

internal sealed record Score(
    [property: JsonPropertyName("fulltime")] ScorePair? Fulltime);

internal sealed record ScorePair(
    [property: JsonPropertyName("home")] int? Home,
    [property: JsonPropertyName("away")] int? Away);
