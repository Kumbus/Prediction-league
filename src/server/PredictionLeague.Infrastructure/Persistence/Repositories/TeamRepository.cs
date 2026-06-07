using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core Team repository. Adds the external-id lookup ingest upserts against.
public class TeamRepository : BaseRepository<Team>, ITeamRepository
{
    public TeamRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Team?> GetByExternalTeamIdAsync(int externalTeamId, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(t => t.ExternalTeamId == externalTeamId, cancellationToken);
}
