using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Player. Ingest resolves by external player id (with a
// minimal-create fallback when a seed is missing).
public interface IPlayerRepository : IRepository<Player>
{
    Task<Player?> GetByExternalPlayerIdAsync(int externalPlayerId, CancellationToken cancellationToken = default);

    // Paged list for the admin Players table.
    Task<PagedResult<Player>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    // CSV-import exact-match lookup: (Name, NationalityId) is the natural upsert key.
    Task<Player?> FindByNameAndNationalityAsync(string name, int nationalityId, CancellationToken cancellationToken = default);
}
