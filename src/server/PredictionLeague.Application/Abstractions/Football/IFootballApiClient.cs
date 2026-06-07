namespace PredictionLeague.Application.Abstractions.Football;

// Persistence-style abstraction over API-Football. The ingest service depends on this,
// not on the concrete HttpClient. Implementations enforce the envelope/quota rules.
public interface IFootballApiClient
{
    // GET /fixtures?league={leagueId}&season={season}&date={date} — a day's slate + scores.
    Task<FixturesResponse> GetFixturesAsync(string leagueId, int season, DateOnly date, CancellationToken cancellationToken = default);

    // GET /fixtures/events?fixture={fixtureId} — goal/card events for one fixture.
    Task<EventsResponse> GetFixtureEventsAsync(int fixtureId, CancellationToken cancellationToken = default);
}
