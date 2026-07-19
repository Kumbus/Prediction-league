using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Team. Ingest upserts by external team id.
public interface ITeamRepository : IRepository<Team>
{
    Task<Team?> GetByExternalTeamIdAsync(int externalTeamId, CancellationToken cancellationToken = default);

    // Alphabetical list backing the admin team pickers.
    Task<IReadOnlyList<Team>> ListAsync(CancellationToken cancellationToken = default);

    // Case-insensitive name lookup — the manual-match CSV importer resolves/creates teams by name.
    Task<Team?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}
