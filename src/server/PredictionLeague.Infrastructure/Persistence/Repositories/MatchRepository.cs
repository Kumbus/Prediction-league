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
}
