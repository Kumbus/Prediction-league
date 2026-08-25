using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core Match repository. The external-fixture lookup includes Events so ingest can
// delete-and-replace them in one tracked graph.
public class MatchRepository : BaseRepository<Match>, IMatchRepository
{
    public MatchRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Match?> GetByExternalFixtureIdAsync(int externalFixtureId, CancellationToken cancellationToken = default)
        => await Set
            .Include(m => m.Events)
            .FirstOrDefaultAsync(m => m.ExternalFixtureId == externalFixtureId, cancellationToken);

    public async Task<Match?> GetWithEventsAsync(Guid matchId, CancellationToken cancellationToken = default)
        => await Set
            .Include(m => m.Events)
            .FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);

    // Ordered by (Minute, MinuteExtra) for reading. This is presentation order only — the scoring
    // engine re-derives its own first-scorer order in memory from a fuller key (MatchOutcome).
    public async Task<IReadOnlyList<MatchEventEditDto>> ListEventsForEditAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var query =
            from e in Context.MatchEvents.AsNoTracking().Where(e => e.MatchId == matchId)
            join p in Context.Players.AsNoTracking() on e.PlayerId equals p.Id
            join t in Context.Teams.AsNoTracking() on e.TeamId equals t.Id
            join et in Context.MatchEventTypes.AsNoTracking() on e.MatchEventTypeId equals et.Id
            orderby e.Minute, e.MinuteExtra
            select new MatchEventEditDto(
                e.Id, et.Id, et.Code, et.DisplayName, p.Id, p.Name, t.Id, t.Name, e.Minute, e.MinuteExtra);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task ReplaceEventsAsync(Guid matchId, IReadOnlyList<MatchEvent> events, CancellationToken cancellationToken = default)
    {
        var match = await Set
            .Include(m => m.Events)
            .FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken)
            ?? throw new InvalidOperationException($"Match '{matchId}' not found.");

        match.Events.Clear(); // orphaned required dependents are deleted on SaveChanges

        foreach (var e in events)
        {
            e.MatchId = match.Id;
            match.Events.Add(e);

            // Explicit Add: callers build events with an Id already set, and against a tracked
            // Unchanged match EF's IsKeySet heuristic reads a key-set child as an existing row and
            // marks it Modified — an UPDATE on a row that was never inserted.
            Context.Set<MatchEvent>().Add(e);
        }
    }

    public async Task<IReadOnlyList<MatchWithEventsDto>> ListByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var query =
            from m in Context.Matches.Where(m => m.TournamentId == tournamentId)
            join home in Context.Teams on m.HomeTeamId equals home.Id
            join away in Context.Teams on m.AwayTeamId equals away.Id
            orderby m.KickoffUtc
            select new MatchWithEventsDto(
                m.Id,
                m.ExternalFixtureId,
                m.KickoffUtc,
                m.Status,
                new TeamRefDto(home.Id, home.Name, m.HomeScore),
                new TeamRefDto(away.Id, away.Name, m.AwayScore),
                (from e in Context.MatchEvents.Where(e => e.MatchId == m.Id)
                 join p in Context.Players on e.PlayerId equals p.Id
                 join t in Context.Teams on e.TeamId equals t.Id
                 join et in Context.MatchEventTypes on e.MatchEventTypeId equals et.Id
                 orderby e.Minute, e.MinuteExtra
                 select new MatchEventDto(e.Minute, e.MinuteExtra, et.Code, et.Category, p.Name, t.Name)
                ).ToList());

        return await query.ToListAsync(cancellationToken);
    }

    // Same team joins as ListByTournamentAsync, without the events — the predictions screen never
    // renders them and the lock check only needs KickoffUtc. Ordered by kickoff then match id so
    // two fixtures at the same instant keep a stable order across calls.
    public async Task<IReadOnlyList<MatchRoundDto>> ListForPredictionsAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var query =
            from m in Context.Matches.AsNoTracking().Where(m => m.TournamentId == tournamentId)
            join home in Context.Teams.AsNoTracking() on m.HomeTeamId equals home.Id
            join away in Context.Teams.AsNoTracking() on m.AwayTeamId equals away.Id
            orderby m.KickoffUtc, m.Id
            select new MatchRoundDto(
                m.Id,
                m.Round,
                m.KickoffUtc,
                m.Status,
                new TeamRefDto(home.Id, home.Name, m.HomeScore),
                new TeamRefDto(away.Id, away.Name, m.AwayScore));

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyKickedOffAsync(Guid tournamentId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        => await Set.AnyAsync(m => m.TournamentId == tournamentId && m.KickoffUtc <= asOf, cancellationToken);
}
