using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core Tournament repository. Adds the external-id + active-window lookups ingest needs.
public class TournamentRepository : BaseRepository<Tournament>, ITournamentRepository
{
    public TournamentRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Tournament?> GetByExternalApiIdAsync(string externalApiId, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(t => t.ExternalApiId == externalApiId, cancellationToken);

    public async Task<IReadOnlyList<Tournament>> GetActiveAsync(DateOnly onDate, CancellationToken cancellationToken = default)
        => await Set
            .Where(t => t.ExternalApiId != null && t.StartDate <= onDate && onDate <= t.EndDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Tournament>> ListAsync(bool includeUnpublished, CancellationToken cancellationToken = default)
    {
        IQueryable<Tournament> q = Set;
        if (!includeUnpublished)
            q = q.Where(t => t.IsPublished);
        return await q.OrderBy(t => t.StartDate).ToListAsync(cancellationToken);
    }
}
