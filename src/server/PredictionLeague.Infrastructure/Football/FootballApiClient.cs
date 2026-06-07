using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Infrastructure.Football.Dtos;

namespace PredictionLeague.Infrastructure.Football;

// Typed HttpClient for API-Football (api-sports.io v3). Base address + x-apisports-key are
// configured in AddFootballIngest; this class issues the two calls, enforces the envelope
// rules (errors[] = failure even on 200; 204 = valid empty) and surfaces the rate-limit
// quota so the ingest service can stop before exhausting the free-tier budget.
public sealed class FootballApiClient : IFootballApiClient
{
    // Daily quota header (x-ratelimit-requests-remaining) and per-minute header
    // (X-RateLimit-Remaining), per api-reference.md.
    private const string DailyRemainingHeader = "x-ratelimit-requests-remaining";
    private const string MinuteRemainingHeader = "X-RateLimit-Remaining";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<FootballApiClient> _logger;

    public FootballApiClient(HttpClient httpClient, ILogger<FootballApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<FixturesResponse> GetFixturesAsync(string leagueId, int season, DateOnly date, CancellationToken cancellationToken = default)
    {
        var requestUri = $"fixtures?league={Uri.EscapeDataString(leagueId)}&season={season}&date={date:yyyy-MM-dd}";
        var (items, rateLimit, _) = await SendAsync<FixtureItem>(requestUri, cancellationToken);
        return new FixturesResponse(items.Select(MapFixture).ToList(), rateLimit);
    }

    public async Task<EventsResponse> GetFixtureEventsAsync(int fixtureId, CancellationToken cancellationToken = default)
    {
        var requestUri = $"fixtures/events?fixture={fixtureId}";
        var (items, rateLimit, noContent) = await SendAsync<EventItem>(requestUri, cancellationToken);
        return new EventsResponse(items.Select(MapEvent).ToList(), noContent, rateLimit);
    }

    // Wire → provider-neutral mapping. Keeps the vendor JSON shape from leaking past this client.
    private static IngestFixture MapFixture(FixtureItem item) => new(
        FixtureId: item.Fixture?.Id ?? 0,
        KickoffUtc: item.Fixture?.Date ?? default,
        StatusShort: item.Fixture?.Status?.Short,
        Season: item.League?.Season ?? 0,
        Round: item.League?.Round,
        Home: MapTeam(item.Teams?.Home),
        Away: MapTeam(item.Teams?.Away),
        GoalsHome: item.Goals?.Home,
        GoalsAway: item.Goals?.Away,
        FulltimeHome: item.Score?.Fulltime?.Home,
        FulltimeAway: item.Score?.Fulltime?.Away);

    private static IngestEvent MapEvent(EventItem item) => new(
        Minute: item.Time?.Elapsed,
        MinuteExtra: item.Time?.Extra,
        Team: MapTeam(item.Team),
        PlayerId: item.Player?.Id,
        PlayerName: item.Player?.Name,
        Type: item.Type,
        Detail: item.Detail);

    private static IngestTeamRef? MapTeam(TeamRefWire? team) =>
        team is null ? null : new IngestTeamRef(team.Id, team.Name, team.Logo);

    private async Task<(IReadOnlyList<T> Items, RateLimitSnapshot RateLimit, bool NoContent)> SendAsync<T>(
        string requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);

        var rateLimit = ReadRateLimit(response);

        // 204 = valid empty result (e.g. events before kickoff), not an error.
        if (response.StatusCode == HttpStatusCode.NoContent)
            return ([], rateLimit, true);

        if (!response.IsSuccessStatusCode)
            throw new FootballApiException(
                $"API-Football returned {(int)response.StatusCode} {response.ReasonPhrase} for '{requestUri}'.");

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions, cancellationToken);
        if (envelope is null)
            throw new FootballApiException($"API-Football returned an unreadable body for '{requestUri}'.");

        var errors = envelope.GetErrorMessages();
        if (errors.Count > 0)
            throw new FootballApiException(
                $"API-Football reported errors for '{requestUri}': {string.Join("; ", errors)}");

        return (envelope.Response, rateLimit, false);
    }

    private RateLimitSnapshot ReadRateLimit(HttpResponseMessage response)
    {
        var daily = ReadIntHeader(response, DailyRemainingHeader);
        var minute = ReadIntHeader(response, MinuteRemainingHeader);

        if (daily is not null && daily <= 0)
            _logger.LogWarning("API-Football daily quota exhausted (remaining={Remaining}).", daily);

        return new RateLimitSnapshot(daily, minute);
    }

    private static int? ReadIntHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values) &&
            int.TryParse(values.FirstOrDefault(), out var parsed))
            return parsed;

        return null;
    }
}
