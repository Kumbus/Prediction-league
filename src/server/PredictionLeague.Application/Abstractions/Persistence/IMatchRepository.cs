using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Match. Ingest upserts by external fixture id.
public interface IMatchRepository : IRepository<Match>
{
    // Looks the fixture up by its API id, including its Events (delete-and-replace target).
    Task<Match?> GetByExternalFixtureIdAsync(int externalFixtureId, CancellationToken cancellationToken = default);

    // Read-side projection for the admin tournament-detail page (resolves team + player +
    // event-type names without nav properties on the entities).
    Task<IReadOnlyList<MatchWithEventsDto>> ListByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // Read-side projection backing the predictions screen: every match in the tournament with its
    // Round and no events, ordered by kickoff then id (stable when two kick off together). One
    // read serves both the round view and the batch write's lock check, so both compare against
    // the same KickoffUtc.
    Task<IReadOnlyList<MatchRoundDto>> ListForPredictionsAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // True when at least one match in the tournament has kicked off by asOf. The clock is a
    // parameter, not an internal UtcNow, so the caller owns "now" (S-04 scoring lock).
    Task<bool> AnyKickedOffAsync(Guid tournamentId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
