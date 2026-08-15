using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core Player repository. Adds the external-id lookup ingest resolves events against.
public class PlayerRepository : BaseRepository<Player>, IPlayerRepository
{
    public PlayerRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Player?> GetByExternalPlayerIdAsync(int externalPlayerId, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(p => p.ExternalPlayerId == externalPlayerId, cancellationToken);

    public async Task<PagedResult<Player>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var total = await Set.CountAsync(cancellationToken);
        var items = await Set
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Player>(items, total, page, pageSize);
    }

    public async Task<Player?> FindByNameAndNationalityAsync(string name, int nationalityId, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(
            p => p.Name == name && p.NationalityId == nationalityId,
            cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<EligibleScorerDto>>> ListEligibleScorersByTeamAsync(
        Guid tournamentId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default)
    {
        var ids = teamIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, IReadOnlyList<EligibleScorerDto>>();

        // Both team columns are nullable, so the IN list is too — that keeps the whole predicate
        // in SQL instead of forcing a null guard EF would have to translate around.
        var nullableIds = ids.Select(id => (Guid?)id).ToList();
        var query = Set
            .AsNoTracking()
            .Where(p => nullableIds.Contains(p.ClubTeamId) || nullableIds.Contains(p.NationalTeamId));

        // The squad narrows the set only when it exists. Probing it separately keeps the empty
        // case a *widening* rather than an intersection with nothing. One probe per request: the
        // answer cannot differ between two teams of the same tournament.
        var hasSquad = await Context.TournamentSquads
            .AsNoTracking()
            .AnyAsync(s => s.TournamentId == tournamentId, cancellationToken);
        if (hasSquad)
            query = query.Where(p => Context.TournamentSquads
                .Any(s => s.TournamentId == tournamentId && s.PlayerId == p.Id));

        var players = await query
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.ClubTeamId, p.NationalTeamId })
            .ToListAsync(cancellationToken);

        // Fanned out per team in memory — a player attached to two teams in the set belongs to
        // both lists, and which one a match shows them under is the caller's call.
        return ids.ToDictionary(
            teamId => teamId,
            teamId => (IReadOnlyList<EligibleScorerDto>)players
                .Where(p => p.ClubTeamId == teamId || p.NationalTeamId == teamId)
                .Select(p => new EligibleScorerDto(p.Id, p.Name, teamId))
                .ToList());
    }
}
