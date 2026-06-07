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
}
