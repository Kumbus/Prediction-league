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

    // Players who may be forecast as a first scorer, keyed by team: attached to a team via
    // ClubTeamId or NationalTeamId, narrowed to the tournament's squad *only when that squad has
    // rows* — TournamentSquad is optional and frequently empty, and an empty squad must widen to
    // the team-derived set rather than reject every scorer.
    //
    // Keyed by team rather than by match, and taking the whole team set at once, so a round's
    // candidates cost two queries per request instead of two per match. A team with no linked
    // players is present in the result with an empty list.
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<EligibleScorerDto>>> ListEligibleScorersByTeamAsync(
        Guid tournamentId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default);
}
