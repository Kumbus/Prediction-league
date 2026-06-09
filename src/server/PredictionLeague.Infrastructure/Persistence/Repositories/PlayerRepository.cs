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
}
