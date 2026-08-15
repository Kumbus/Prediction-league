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

    public async Task<IReadOnlyList<EligibleScorerDto>> ListEligibleScorersAsync(
        Guid tournamentId,
        Guid homeTeamId,
        Guid awayTeamId,
        CancellationToken cancellationToken = default)
    {
        var query = Set
            .AsNoTracking()
            .Where(p => p.ClubTeamId == homeTeamId || p.NationalTeamId == homeTeamId
                     || p.ClubTeamId == awayTeamId || p.NationalTeamId == awayTeamId);

        // The squad narrows the set only when it exists. Probing it separately keeps the empty
        // case a *widening* rather than an intersection with nothing.
        var hasSquad = await Context.TournamentSquads
            .AsNoTracking()
            .AnyAsync(s => s.TournamentId == tournamentId, cancellationToken);
        if (hasSquad)
            query = query.Where(p => Context.TournamentSquads
                .Any(s => s.TournamentId == tournamentId && s.PlayerId == p.Id));

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new EligibleScorerDto(
                p.Id,
                p.Name,
                // The team the player belongs to among the two. A player attached to both sides
                // (club one, national the other) is listed under the home team — the credited
                // team is a separate choice, so nothing about the forecast is lost.
                p.ClubTeamId == homeTeamId || p.NationalTeamId == homeTeamId ? homeTeamId : awayTeamId))
            .ToListAsync(cancellationToken);
    }
}
