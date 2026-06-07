using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Player. Ingest resolves by external player id (with a
// minimal-create fallback when a seed is missing).
public interface IPlayerRepository : IRepository<Player>
{
    Task<Player?> GetByExternalPlayerIdAsync(int externalPlayerId, CancellationToken cancellationToken = default);
}
