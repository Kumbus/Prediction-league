using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-tournament roster join entity. CSV importer upserts rows here when given a tournamentId.
// No SaveChanges on this repo — the import service owns the transaction.
public interface ITournamentSquadRepository
{
    Task<bool> ExistsAsync(Guid tournamentId, Guid playerId, CancellationToken cancellationToken = default);

    Task AddAsync(TournamentSquad entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TournamentSquad>> ListByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);
}
