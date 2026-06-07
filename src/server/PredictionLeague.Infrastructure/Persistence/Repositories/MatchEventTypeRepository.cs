using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core MatchEventType dictionary repository. Adds the by-code lookup the ingest
// detail → Code mapping resolves against.
public class MatchEventTypeRepository : BaseRepository<MatchEventType>, IMatchEventTypeRepository
{
    public MatchEventTypeRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<MatchEventType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(t => t.Code == code, cancellationToken);
}
