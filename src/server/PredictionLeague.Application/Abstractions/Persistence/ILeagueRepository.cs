using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for League. Add league-specific queries here as slices need them
// (e.g. GetByInviteCodeAsync for S-03); none required by F-01.
public interface ILeagueRepository : IRepository<League>
{
    // True if any League row references the given tournament. Used by tournament delete to
    // refuse cascading away leagues.
    Task<bool> AnyForTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);
}
