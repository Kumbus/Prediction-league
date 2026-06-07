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
        var fixturesUpserted = 0;
        var eventsUpserted = 0;

        foreach (var fixture in fixturesResponse.Fixtures)
        {
            if (fixture.Fixture is null || fixture.Teams?.Home is null || fixture.Teams?.Away is null)
            {
                _logger.LogWarning("Skipping fixture with missing core data (id={Id}).", fixture.Fixture?.Id);
                continue;
            }

            var status = MapStatus(fixture.Fixture.Status?.Short);

            var homeTeamId = await ResolveTeamAsync(fixture.Teams.Home, teamCache, cancellationToken);
            var awayTeamId = await ResolveTeamAsync(fixture.Teams.Away, teamCache, cancellationToken);

            var match = await _matches.GetByExternalFixtureIdAsync(fixture.Fixture.Id, cancellationToken);
            var isNew = match is null;
            if (match is null)
            {
                match = new Match
                {
                    Id = Guid.NewGuid(),
                    ExternalFixtureId = fixture.Fixture.Id,
                    Round = fixture.League?.Round ?? string.Empty
                };
            }

            match.TournamentId = tournament.Id;
            match.Season = fixture.League?.Season ?? season;
            match.Round = fixture.League?.Round ?? match.Round;
            match.HomeTeamId = homeTeamId;
            match.AwayTeamId = awayTeamId;
            match.KickoffUtc = fixture.Fixture.Date;
            match.Status = status;

            // Score source by status: fulltime when finished, running goals otherwise.
            if (status == MatchStatus.Finished)
            {
                match.HomeScore = fixture.Score?.Fulltime?.Home ?? fixture.Goals?.Home;
                match.AwayScore = fixture.Score?.Fulltime?.Away ?? fixture.Goals?.Away;
            }
            else
            {
                match.HomeScore = fixture.Goals?.Home;
                match.AwayScore = fixture.Goals?.Away;
            }

            if (isNew)
                await _matches.AddAsync(match, cancellationToken);
            else
                _matches.Update(match);

            // Events only for finished/in-play fixtures, and only while quota allows.
            if (status is MatchStatus.Finished or MatchStatus.Live)
            {
                if (quotaRemaining is not null && quotaRemaining <= MinQuotaBuffer)
                {
                    _logger.LogWarning(
                        "Quota low (remaining={Remaining}); skipping events for fixture {FixtureId} and onward.",
                        quotaRemaining, fixture.Fixture.Id);
                }
                else
                {
                    var eventsResponse = await _apiClient.GetFixtureEventsAsync(fixture.Fixture.Id, cancellationToken);
                    apiCallsUsed++;
                    quotaRemaining = eventsResponse.RateLimit.DailyRemaining ?? quotaRemaining;

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
        IReadOnlyList<EventDto> events,
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
            if (ev.Player?.Id is null)
                continue;

            var code = MapDetailToCode(ev.Detail);
            if (code is null || !eventTypeIdByCode.TryGetValue(code, out var eventTypeId))
            {
                _logger.LogWarning(
                    "Unmapped event detail '{Detail}' (type '{Type}') on fixture {FixtureId}; skipping.",
                    ev.Detail, ev.Type, match.ExternalFixtureId);
                continue;
            }

            if (ev.Team?.Id is null)
            {
                _logger.LogWarning(
                    "Event with no team on fixture {FixtureId}; skipping.", match.ExternalFixtureId);
                continue;
            }

            var teamId = await ResolveTeamAsync(ev.Team, teamCache, cancellationToken);
            var playerId = await ResolvePlayerAsync(ev.Player, teamCache, playerCache, cancellationToken);

            match.Events.Add(new MatchEvent
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                MatchEventTypeId = eventTypeId,
                PlayerId = playerId,
                TeamId = teamId,
                Minute = ev.Time?.Elapsed ?? 0,
                MinuteExtra = ev.Time?.Extra
            });
            added++;
        }

        return added;
    }

    private async Task<Guid> ResolveTeamAsync(TeamRefDto team, Dictionary<int, Guid> cache, CancellationToken cancellationToken)
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
            LogoUrl = team.Logo
        };
        await _teams.AddAsync(created, cancellationToken);
        cache[team.Id] = created.Id;
        return created.Id;
    }

    // Resolve by external id; minimal-create fallback so a missing seed never drops an
    // event. Does not attempt club/national classification.
    private async Task<Guid> ResolvePlayerAsync(
        PlayerRefDto player, Dictionary<int, Guid> teamCache, Dictionary<int, Guid> playerCache, CancellationToken cancellationToken)
    {
        var externalId = player.Id!.Value;
        if (playerCache.TryGetValue(externalId, out var cached))
            return cached;

        var existing = await _players.GetByExternalPlayerIdAsync(externalId, cancellationToken);
        if (existing is not null)
        {
            playerCache[externalId] = existing.Id;
            return existing.Id;
        }

        _logger.LogWarning(
            "Player {ExternalId} ('{Name}') not seeded; creating minimal record.", externalId, player.Name);

        var created = new Player
        {
            Id = Guid.NewGuid(),
            ExternalPlayerId = externalId,
            Name = player.Name ?? $"Player {externalId}"
        };
        await _players.AddAsync(created, cancellationToken);
        playerCache[externalId] = created.Id;
        return created.Id;
    }

    private static MatchStatus MapStatus(string? statusShort) => statusShort switch
    {
        "FT" or "AET" or "PEN" => MatchStatus.Finished,
        "NS" or "TBD" => MatchStatus.Scheduled,
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
