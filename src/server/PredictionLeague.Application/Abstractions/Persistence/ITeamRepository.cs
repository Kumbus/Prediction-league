using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Team. Ingest upserts by external team id.
public interface ITeamRepository : IRepository<Team>
{
    Task<Team?> GetByExternalTeamIdAsync(int externalTeamId, CancellationToken cancellationToken = default);
}
