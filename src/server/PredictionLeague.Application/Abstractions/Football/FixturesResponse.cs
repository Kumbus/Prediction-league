namespace PredictionLeague.Application.Abstractions.Football;

// Provider-neutral result of a fixtures fetch: items mapped off the vendor wire shape plus
// the quota snapshot. The API-Football JSON DTOs stay internal to Infrastructure so this
// abstraction does not leak the vendor's response shape into Application.
public sealed record FixturesResponse(IReadOnlyList<IngestFixture> Fixtures, RateLimitSnapshot RateLimit);

// One fixture, flattened to what ingest needs. Nullable wherever the source can omit a value.
public sealed record IngestFixture(
    int FixtureId,
    DateTimeOffset KickoffUtc,
    string? StatusShort,
    int Season,
    string? Round,
    IngestTeamRef? Home,
    IngestTeamRef? Away,
    int? GoalsHome,
    int? GoalsAway,
    int? FulltimeHome,
    int? FulltimeAway);

// A team reference carried on a fixture or an event.
public sealed record IngestTeamRef(int Id, string? Name, string? LogoUrl);
