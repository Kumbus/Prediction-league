using Microsoft.Extensions.Logging;
using PredictionLeague.Application.Abstractions.Football;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Football;

// The single shared ingest orchestration: pull a tournament's fixtures + events, map the
// DTOs into the relational model, and upsert idempotently through the F-01 repos. Resolved
// from DI by both the Functions timer and the Api manual-trigger endpoint.
public sealed class FixtureIngestService : IFixtureIngestService
{
    // Keep a small buffer under the free-tier daily cap; stop issuing event calls when the
    // known remaining quota drops to/below this rather than tight-retrying.
    private const int MinQuotaBuffer = 1;

    // Same idea against the per-minute cap (10/min free tier): stop issuing event calls when
    // the minute window is nearly spent rather than tight-retrying into a 429.
    private const int MinMinuteBuffer = 1;

    private readonly IFootballApiClient _apiClient;
    private readonly ITournamentRepository _tournaments;
    private readonly IMatchRepository _matches;
    private readonly ITeamRepository _teams;
    private readonly IPlayerRepository _players;
    private readonly IMatchEventTypeRepository _eventTypes;
    private readonly ILogger<FixtureIngestService> _logger;

    public FixtureIngestService(
        IFootballApiClient apiClient,
        ITournamentRepository tournaments,
        IMatchRepository matches,
        ITeamRepository teams,
        IPlayerRepository players,
        IMatchEventTypeRepository eventTypes,
        ILogger<FixtureIngestService> logger)
    {
        _apiClient = apiClient;
        _tournaments = tournaments;
        _matches = matches;
        _teams = teams;
        _players = players;
        _eventTypes = eventTypes;
        _logger = logger;
    }

    public async Task<IngestResult> IngestTournamentAsync(Guid tournamentId, int season, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var tournament = await _tournaments.GetByIdAsync(tournamentId, cancellationToken)
            ?? throw new InvalidOperationException($"Tournament '{tournamentId}' not found.");

        if (string.IsNullOrWhiteSpace(tournament.ExternalApiId))
            throw new InvalidOperationException(
                $"Tournament '{tournamentId}' has no ExternalApiId; cannot ingest.");

        var onDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Dictionary code → id, loaded once (seeded, tiny).
        var eventTypeIdByCode = (await _eventTypes.GetAllAsync(cancellationToken))
            .ToDictionary(t => t.Code, t => t.Id);

        // Per-run caches keyed by external id so a team/player created for one fixture is
        // reused by the next before it is queryable from the DB.
        var teamCache = new Dictionary<int, Guid>();
        var playerCache = new Dictionary<int, Guid>();

        var fixturesResponse = await _apiClient.GetFixturesAsync(
            tournament.ExternalApiId, season, onDate, cancellationToken);

        var apiCallsUsed = 1;
        var quotaRemaining = fixturesResponse.RateLimit.DailyRemaining;
        var minuteRemaining = fixturesResponse.RateLimit.MinuteRemaining;
        var fixturesUpserted = 0;
        var eventsUpserted = 0;

        foreach (var fixture in fixturesResponse.Fixtures)
        {
            if (fixture.FixtureId == 0 || fixture.Home is null || fixture.Away is null)
            {
                _logger.LogWarning("Skipping fixture with missing core data (id={Id}).", fixture.FixtureId);
                continue;
            }

            var status = MapStatus(fixture.StatusShort);

            var homeTeamId = await ResolveTeamAsync(fixture.Home, teamCache, cancellationToken);
            var awayTeamId = await ResolveTeamAsync(fixture.Away, teamCache, cancellationToken);

            var match = await _matches.GetByExternalFixtureIdAsync(fixture.FixtureId, cancellationToken);
            var isNew = match is null;
            if (match is null)
            {
                match = new Match
                {
                    Id = Guid.NewGuid(),
                    ExternalFixtureId = fixture.FixtureId,
                    Round = fixture.Round ?? string.Empty
                };
            }

            match.TournamentId = tournament.Id;
            match.Season = fixture.Season != 0 ? fixture.Season : season;
            match.Round = fixture.Round ?? match.Round;
            match.HomeTeamId = homeTeamId;
            match.AwayTeamId = awayTeamId;
            match.KickoffUtc = fixture.KickoffUtc;
            match.Status = status;

            // Score source by status: fulltime when finished, running goals otherwise.
            if (status == MatchStatus.Finished)
            {
                match.HomeScore = fixture.FulltimeHome ?? fixture.GoalsHome;
                match.AwayScore = fixture.FulltimeAway ?? fixture.GoalsAway;
            }
            else
            {
                match.HomeScore = fixture.GoalsHome;
                match.AwayScore = fixture.GoalsAway;
            }

            if (isNew)
                await _matches.AddAsync(match, cancellationToken);
            else
                _matches.Update(match);

            // Events only for finished/in-play fixtures, and only while quota allows.
            if (status is MatchStatus.Finished or MatchStatus.Live)
            {
                var dailyLow = quotaRemaining is not null && quotaRemaining <= MinQuotaBuffer;
                var minuteLow = minuteRemaining is not null && minuteRemaining <= MinMinuteBuffer;
                if (dailyLow || minuteLow)
                {
                    _logger.LogWarning(
                        "Rate limit low (daily={Daily}, minute={Minute}); skipping events for fixture {FixtureId} and onward.",
                        quotaRemaining, minuteRemaining, fixture.FixtureId);
                }
                else
                {
                    var eventsResponse = await _apiClient.GetFixtureEventsAsync(fixture.FixtureId, cancellationToken);
                    apiCallsUsed++;
                    quotaRemaining = eventsResponse.RateLimit.DailyRemaining ?? quotaRemaining;
                    minuteRemaining = eventsResponse.RateLimit.MinuteRemaining ?? minuteRemaining;

                    eventsUpserted += await ReplaceEventsAsync(
                        match, eventsResponse.Events, eventTypeIdByCode, teamCache, playerCache, cancellationToken);
                }
            }

            // One SaveChanges per match (scoped transaction) — a partial run leaves each
            // processed match fully consistent.
            await _matches.SaveChangesAsync(cancellationToken);
            fixturesUpserted++;
        }

        _logger.LogInformation(
            "Ingest tournament {TournamentId} ({Date}): {Fixtures} fixtures, {Events} events, {Calls} API calls, quota remaining {Quota}.",
            tournamentId, onDate, fixturesUpserted, eventsUpserted, apiCallsUsed, quotaRemaining);

        return new IngestResult(fixturesUpserted, eventsUpserted, apiCallsUsed, quotaRemaining);
    }

    // Delete-and-replace: API events carry no stable id, so the whole set is rebuilt each
    // ingest. Filters to Goal/Card, skips partial (null type/player) entries.
    private async Task<int> ReplaceEventsAsync(
        Match match,
        IReadOnlyList<IngestEvent> events,
        IReadOnlyDictionary<string, int> eventTypeIdByCode,
        Dictionary<int, Guid> teamCache,
        Dictionary<int, Guid> playerCache,
        CancellationToken cancellationToken)
    {
        match.Events.Clear(); // orphaned required dependents are deleted on SaveChanges

        var added = 0;
        foreach (var ev in events)
        {
            // Only Goal/Card are seeded as dictionary rows; Subst/Var have no MatchEventTypeId.
            if (!IsGoalOrCard(ev.Type))
                continue;

            // Trailing partial array entries (null type/player) cannot satisfy the non-null
            // PlayerId FK — skip; the minimal-create fallback only covers a present id.
            if (ev.PlayerId is null)
                continue;

            var code = MapDetailToCode(ev.Detail);
            if (code is null || !eventTypeIdByCode.TryGetValue(code, out var eventTypeId))
            {
                _logger.LogWarning(
                    "Unmapped event detail '{Detail}' (type '{Type}') on fixture {FixtureId}; skipping.",
                    ev.Detail, ev.Type, match.ExternalFixtureId);
                continue;
            }

            if (ev.Team is null)
            {
                _logger.LogWarning(
                    "Event with no team on fixture {FixtureId}; skipping.", match.ExternalFixtureId);
                continue;
            }

            var teamId = await ResolveTeamAsync(ev.Team, teamCache, cancellationToken);
            var playerId = await ResolvePlayerAsync(ev.PlayerId.Value, ev.PlayerName, playerCache, cancellationToken);

            match.Events.Add(new MatchEvent
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                MatchEventTypeId = eventTypeId,
                PlayerId = playerId,
                TeamId = teamId,
                Minute = ev.Minute ?? 0,
                MinuteExtra = ev.MinuteExtra
            });
            added++;
        }

        return added;
    }

    private async Task<Guid> ResolveTeamAsync(IngestTeamRef team, Dictionary<int, Guid> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(team.Id, out var cached))
            return cached;

        var existing = await _teams.GetByExternalTeamIdAsync(team.Id, cancellationToken);
        if (existing is not null)
        {
            cache[team.Id] = existing.Id;
            return existing.Id;
        }

        var created = new Team
        {
            Id = Guid.NewGuid(),
            ExternalTeamId = team.Id,
            Name = team.Name ?? $"Team {team.Id}",
            LogoUrl = team.LogoUrl
        };
        await _teams.AddAsync(created, cancellationToken);
        cache[team.Id] = created.Id;
        return created.Id;
    }

    // Resolve by external id; minimal-create fallback so a missing seed never drops an
    // event. Does not attempt club/national classification.
    private async Task<Guid> ResolvePlayerAsync(
        int externalId, string? playerName, Dictionary<int, Guid> playerCache, CancellationToken cancellationToken)
    {
        if (playerCache.TryGetValue(externalId, out var cached))
            return cached;

        var existing = await _players.GetByExternalPlayerIdAsync(externalId, cancellationToken);
        if (existing is not null)
        {
            playerCache[externalId] = existing.Id;
            return existing.Id;
        }

        _logger.LogWarning(
            "Player {ExternalId} ('{Name}') not seeded; creating minimal record.", externalId, playerName);

        var created = new Player
        {
            Id = Guid.NewGuid(),
            ExternalPlayerId = externalId,
            Name = playerName ?? $"Player {externalId}"
        };
        await _players.AddAsync(created, cancellationToken);
        playerCache[externalId] = created.Id;
        return created.Id;
    }

    private static MatchStatus MapStatus(string? statusShort) => statusShort switch
    {
        "FT" or "AET" or "PEN" => MatchStatus.Finished,
        // Not-yet-played and non-played terminals: never spend an events call on them.
        "NS" or "TBD" or "PST" or "CANC" or "ABD" or "AWD" or "WO" or "SUSP" or "INT" => MatchStatus.Scheduled,
        // True in-play only (1H/HT/2H/ET/BT/P/LIVE).
        _ => MatchStatus.Live
    };

    private static bool IsGoalOrCard(string? type) =>
        string.Equals(type, "Goal", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Card", StringComparison.OrdinalIgnoreCase);

    // API detail carries spaces; seed Code is space-free. Explicit map (not strip) so an
    // unexpected detail surfaces as a skip+warn rather than a silent miss.
    private static string? MapDetailToCode(string? detail) => detail switch
    {
        "Normal Goal" => "NormalGoal",
        "Own Goal" => "OwnGoal",
        "Penalty" => "Penalty",
        "Missed Penalty" => "MissedPenalty",
        "Yellow Card" => "YellowCard",
        "Red Card" => "RedCard",
        _ => null
    };
}
