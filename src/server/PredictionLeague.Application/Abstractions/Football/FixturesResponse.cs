using System.Text.Json.Serialization;

namespace PredictionLeague.Application.Abstractions.Football;

// Typed result of GET /fixtures: the response items plus the quota snapshot.
public sealed record FixturesResponse(IReadOnlyList<FixtureDto> Fixtures, RateLimitSnapshot RateLimit);

// One /fixtures response item. Nullable wherever the API can omit a value.
public sealed record FixtureDto(
    [property: JsonPropertyName("fixture")] FixtureInfoDto? Fixture,
    [property: JsonPropertyName("league")] FixtureLeagueDto? League,
    [property: JsonPropertyName("teams")] FixtureTeamsDto? Teams,
    [property: JsonPropertyName("goals")] GoalsDto? Goals,
    [property: JsonPropertyName("score")] ScoreDto? Score);

public sealed record FixtureInfoDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("status")] FixtureStatusDto? Status);

public sealed record FixtureStatusDto(
    [property: JsonPropertyName("short")] string? Short,
    [property: JsonPropertyName("elapsed")] int? Elapsed,
    [property: JsonPropertyName("extra")] int? Extra);

public sealed record FixtureLeagueDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("season")] int Season,
    [property: JsonPropertyName("round")] string? Round);

public sealed record FixtureTeamsDto(
    [property: JsonPropertyName("home")] TeamRefDto? Home,
    [property: JsonPropertyName("away")] TeamRefDto? Away);

public sealed record TeamRefDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("logo")] string? Logo);

public sealed record GoalsDto(
    [property: JsonPropertyName("home")] int? Home,
    [property: JsonPropertyName("away")] int? Away);

public sealed record ScoreDto(
    [property: JsonPropertyName("fulltime")] ScorePairDto? Fulltime);

public sealed record ScorePairDto(
    [property: JsonPropertyName("home")] int? Home,
    [property: JsonPropertyName("away")] int? Away);
