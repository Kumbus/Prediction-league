using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for League. Add league-specific queries here as slices need them
// (e.g. GetByInviteCodeAsync for S-05); S-03 added the caller-scoped list, the detail read,
// and the invite-code existence probe.
public interface ILeagueRepository : IRepository<League>
{
    // True if any League row references the given tournament. Used by tournament delete to
    // refuse cascading away leagues.
    Task<bool> AnyForTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // Leagues the user organizes or is a member of, ordered by name (FR-006).
    Task<IReadOnlyList<League>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    // One league with its scoring config and memberships. Not user-scoped — the caller decides
    // whether the requester may see it.
    Task<League?> GetWithDetailAsync(Guid leagueId, CancellationToken cancellationToken = default);

    // Pre-insert probe for the invite-code generator. The unique index is the real guarantee.
    Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken cancellationToken = default);

    // Persists a new league with its scoring rules and memberships as one unit. Throws
    // InviteCodeCollisionException when the unique index rejects the invite code, so the caller
    // can retry with a fresh one without depending on EF Core's exception types.
    Task CreateAsync(League league, CancellationToken cancellationToken = default);
}
