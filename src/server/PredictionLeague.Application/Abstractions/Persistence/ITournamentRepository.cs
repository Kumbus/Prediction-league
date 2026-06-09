using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Tournament. Ingest looks tournaments up by their external
// API id; the production timer iterates the active set.
public interface ITournamentRepository : IRepository<Tournament>
{
    Task<Tournament?> GetByExternalApiIdAsync(string externalApiId, CancellationToken cancellationToken = default);

    // Tournaments in-window on a given date with a non-null ExternalApiId — the set the
    // scheduled ingest iterates.
    Task<IReadOnlyList<Tournament>> GetActiveAsync(DateOnly onDate, CancellationToken cancellationToken = default);

    // Admin list (includeUnpublished=true) or public list (includeUnpublished=false).
    Task<IReadOnlyList<Tournament>> ListAsync(bool includeUnpublished, CancellationToken cancellationToken = default);
}
